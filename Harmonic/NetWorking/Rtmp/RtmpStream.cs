using Harmonic.Networking;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Exceptions;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Pipelines;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using Harmonic.Networking.Rtmp.Messages;
using Harmonic.Networking.Rtmp.Serialization;
using Harmonic.Networking.Utils;

namespace Harmonic.Networking.Rtmp
{
    // TBD: retransfer bytes when acknowledgement not received
    class RtmpStream : IDisposable
    {
        enum ProcessState
        {
            HandshakeC0C1,
            HandshakeC1,
            HandshakeC2,
            FirstByteBasicHeader,
            ChunkMessageHeader,
            ExtendedTimestamp,
            CompleteMessage
        }

        class MessageReadingState
        {
            public uint MessageLength;
            public byte[] Body;
            public int CurrentIndex;
            public long RemainBytes
            {
                get => MessageLength - CurrentIndex;
            }
            public bool IsCompleted
            {
                get => RemainBytes == 0;
            }
        }

        class WriteState
        {
            public byte[] Buffer;
            public int Length;
            public TaskCompletionSource<int> TaskSource = null;
        }

        private delegate bool BufferProcessor(ReadOnlySequence<byte> buffer, ref int consumed);

        private int ReadMinimumBufferSize { get => (ReadChunkSize + TYPE0_SIZE) * 4; }
        private SemaphoreSlim _writerSignal = new SemaphoreSlim(0);
        private Random _random = new Random();
        private Socket _socket;
        private ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
        private MemoryPool<byte> _memoryPool = MemoryPool<byte>.Shared;
        private Dictionary<uint, MessageHeader> _previousReadMessageHeader = new Dictionary<uint, MessageHeader>();
        private Dictionary<uint, MessageReadingState> _incompleteMessageState = new Dictionary<uint, MessageReadingState>();
        private Dictionary<MessageType, IRtmpMessageIO> _messageIO = new Dictionary<MessageType, IRtmpMessageIO>();
        internal uint? ReadWindowAcknowledgementSize { get; set; } = null;
        internal uint? WriteWindowAcknowledgementSize { get; set; } = null;
        internal uint ReadWindowSize { get; set; } = 0;
        internal uint WriteWindowSize { get; private set; } = 0;
        internal int ReadChunkSize { get; set; } = 128;
        internal bool BandwidthLimited { get; set; } = false;
        private int _writeChunkSize = 128;
        private readonly int EXTENDED_TIMESTAMP_LENGTH = 4;
        private readonly int TYPE0_SIZE = 11;
        private readonly int TYPE1_SIZE = 7;
        private readonly int TYPE2_SIZE = 3;
        private ProcessState _nextProcessState = ProcessState.FirstByteBasicHeader;
        private ChunkHeader _processingChunk = null;
        private readonly int _resumeWriterThreshole;
        private IReadOnlyDictionary<ProcessState, BufferProcessor> _bufferProcessors;
        private uint _readerTimestampEpoch = 0;
        private uint _writerTimestampEpoch = 0;
        private byte[] _s1Data = null;
        private byte[] _c1Data = null;
        private Queue<WriteState> _writerQueue = new Queue<WriteState>();
        RtmpSession _rtmpSession = null;

        public RtmpStream(Socket socket, int resumeWriterThreshole = 65535)
        {
            _socket = socket;
            _resumeWriterThreshole = resumeWriterThreshole;
            var bufferProcessors = new Dictionary<ProcessState, BufferProcessor>();
            bufferProcessors.Add(ProcessState.HandshakeC0C1, ProcessHandshakeC0C1);
            bufferProcessors.Add(ProcessState.HandshakeC2, ProcessHandshakeC2);
            bufferProcessors.Add(ProcessState.ChunkMessageHeader, ProcessChunkMessageHeader);
            bufferProcessors.Add(ProcessState.CompleteMessage, ProcessCompleteMessage);
            bufferProcessors.Add(ProcessState.ExtendedTimestamp, ProcessExtendedTimestamp);
            bufferProcessors.Add(ProcessState.FirstByteBasicHeader, ProcessFirstByteBasicHeader);
            _bufferProcessors = bufferProcessors;
        }

        public Task StartAsync(CancellationToken ct = default)
        {
            var d = PipeOptions.Default;
            var opt = new PipeOptions(
                MemoryPool<byte>.Shared,
                d.ReaderScheduler,
                d.WriterScheduler,
                _resumeWriterThreshole,
                d.ResumeWriterThreshold,
                d.MinimumSegmentSize,
                d.UseSynchronizationContext);
            var pipe = new Pipe(opt);
            var t1 = Producer(_socket, pipe.Writer, ct);
            var t2 = Consumer(pipe.Reader, ct);
            var t3 = Writer();
            ct.Register(() =>
            {
                _rtmpSession.Dispose();
                _rtmpSession = null;
            });
            return Task.WhenAll(t1, t2, t3);
        }

        private void OnHandshakeSuccessful()
        {
            _rtmpSession = new RtmpSession(this);
        }

        #region Sender
        private async Task Writer()
        {
            while (true)
            {
                await _writerSignal.WaitAsync();
                var data = _writerQueue.Dequeue();
                await _socket.SendAsync(data.Buffer.AsMemory(data.Length), SocketFlags.None);
                _arrayPool.Return(data.Buffer);
                data.TaskSource?.SetResult(1);
            }
        }
        #endregion

        #region Receiver
        private async Task Producer(Socket s, PipeWriter writer, CancellationToken ct = default)
        {
            while (ct.IsCancellationRequested)
            {
                var memory = writer.GetMemory(ReadMinimumBufferSize);
                var bytesRead = await s.ReceiveAsync(memory, SocketFlags.None);
                if (bytesRead == 0)
                {
                    break;
                }
                ReadWindowSize += (uint)bytesRead;
                if (ReadWindowAcknowledgementSize.HasValue)
                {
                    if (ReadWindowSize >= ReadWindowAcknowledgementSize)
                    {
                        _rtmpSession?.Acknowledgement(ReadWindowAcknowledgementSize.Value);
                        ReadWindowSize -= ReadWindowAcknowledgementSize.Value;
                    }
                }
                writer.Advance(bytesRead);
                var result = await writer.FlushAsync(ct);
                if (result.IsCompleted || result.IsCanceled)
                {
                    break;
                }
            }

            writer.Complete();
        }

        private async Task Consumer(PipeReader reader, CancellationToken ct = default)
        {
            while (true)
            {
                var result = await reader.ReadAsync(ct);

                var buffer = result.Buffer;
                int consumed = 0;

                while (true)
                {
                    if (!_bufferProcessors[_nextProcessState](buffer, ref consumed))
                    {
                        break;
                    }
                }
                buffer = buffer.Slice(consumed);

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted || result.IsCanceled)
                {
                    break;
                }
            }

            // Mark the PipeReader as complete
            reader.Complete();
        }
        #endregion

        #region Multiplexing
        private Task SendRawData(byte[] data, int length)
        {
            var tcs = new TaskCompletionSource<int>();
            _writerQueue.Enqueue(new WriteState()
            {
                Buffer = data,
                Length = length,
                TaskSource = tcs
            });
            return tcs.Task;
        }

        internal Task MultiplexMessageAsync(uint chunkStreamId, Message message)
        {
            if (!message.MessageHeader.MessageStreamId.HasValue)
            {
                throw new InvalidOperationException("cannot send message that has not attached to a message stream");
            }
            if (!_messageIO.TryGetValue(message.MessageHeader.MessageType, out var io))
            {
                throw new NotSupportedException();
            }
            var ret = new TaskCompletionSource<int>();
            io.GetBytes(_arrayPool, message, out var buffer, out var length);
            try
            {
                message.MessageHeader.MessageLength = length;
                // chunking
                for (int i = 0; i < message.MessageHeader.MessageLength;)
                {
                    _previousReadMessageHeader.TryGetValue(chunkStreamId, out var prevHeader);
                    var chunkHeaderType = SelectChunkType(message.MessageHeader, prevHeader);
                    GenerateBasicHeader(chunkHeaderType, chunkStreamId, out var basicHeader, out var basicHeaderLength);
                    GenerateMesesageHeader(chunkHeaderType, message.MessageHeader, prevHeader, out var messageHeader, out var messageHeaderLength);
                    var headerLength = basicHeaderLength + messageHeaderLength;
                    var bodySize = (int)(length - i >= _writeChunkSize ? _writeChunkSize : length - i);
                    var chunkBuffer = _arrayPool.Rent(headerLength + bodySize);
                    basicHeader.AsSpan(0, basicHeaderLength).CopyTo(chunkBuffer);
                    messageHeader.AsSpan(0, messageHeaderLength).CopyTo(chunkBuffer.AsSpan(basicHeaderLength));
                    _arrayPool.Return(basicHeader);
                    _arrayPool.Return(messageHeader);
                    buffer.AsSpan(i, _writeChunkSize).CopyTo(chunkBuffer.AsSpan(headerLength));
                    i += bodySize;
                    var isLastChunk = message.MessageHeader.MessageLength - i == 0;
                    TaskCompletionSource<int> tcs = null;
                    if (isLastChunk)
                    {
                        tcs = ret;
                    }
                    _writerQueue.Enqueue(new WriteState()
                    {
                        Buffer = chunkBuffer,
                        Length = bodySize,
                        TaskSource = tcs
                    });
                    _writerSignal.Release();
                }
            }
            finally
            {
                _arrayPool.Return(buffer);
            }

            return ret.Task;
        }

        private void GenerateMesesageHeader(ChunkHeaderType chunkHeaderType, MessageHeader header, MessageHeader prevHeader, out byte[] buffer, out int length)
        {
            var timestamp = header.Timestamp;
            switch (chunkHeaderType)
            {
                case ChunkHeaderType.Type0:
                    buffer = _arrayPool.Rent(TYPE0_SIZE + EXTENDED_TIMESTAMP_LENGTH);
                    NetworkBitConverter.TryGetUInt24Bytes(timestamp >= 0xFFFFFF ? 0xFFFFFF : timestamp, buffer.AsSpan(0, 3));
                    NetworkBitConverter.TryGetUInt24Bytes(header.MessageLength, buffer.AsSpan(3, 3));
                    NetworkBitConverter.TryGetBytes((byte)header.MessageType, buffer.AsSpan(6, 1));
                    NetworkBitConverter.TryGetBytes(header.MessageStreamId.Value, buffer.AsSpan(7, 4), true);
                    length = TYPE0_SIZE;
                    break;
                case ChunkHeaderType.Type1:
                    buffer = _arrayPool.Rent(TYPE1_SIZE + EXTENDED_TIMESTAMP_LENGTH);
                    timestamp = prevHeader.Timestamp - timestamp;
                    NetworkBitConverter.TryGetUInt24Bytes(timestamp >= 0xFFFFFF ? 0xFFFFFF : timestamp, buffer.AsSpan(0, 3));
                    NetworkBitConverter.TryGetUInt24Bytes(header.MessageLength, buffer.AsSpan(3, 3));
                    NetworkBitConverter.TryGetBytes((byte)header.MessageType, buffer.AsSpan(6, 1));
                    length = TYPE1_SIZE;
                    break;
                case ChunkHeaderType.Type2:
                    buffer = _arrayPool.Rent(TYPE2_SIZE + EXTENDED_TIMESTAMP_LENGTH);
                    timestamp = prevHeader.Timestamp - timestamp;
                    NetworkBitConverter.TryGetUInt24Bytes(timestamp >= 0xFFFFFF ? 0xFFFFFF : timestamp, buffer.AsSpan(0, 3));
                    length = TYPE2_SIZE;
                    break;
                case ChunkHeaderType.Type3:
                    buffer = _arrayPool.Rent(EXTENDED_TIMESTAMP_LENGTH);
                    length = 0;
                    break;
                default:
                    throw new ArgumentException();
            }
            if (header.Timestamp == 0xFFFFFF)
            {
                NetworkBitConverter.TryGetBytes(timestamp, buffer.AsSpan(length, EXTENDED_TIMESTAMP_LENGTH));
                length += EXTENDED_TIMESTAMP_LENGTH;
            }
        }

        private void GenerateBasicHeader(ChunkHeaderType chunkHeaderType, uint chunkStreamId, out byte[] buffer, out int length)
        {
            byte fmt = (byte)chunkHeaderType;
            if (chunkStreamId >= 2 && chunkStreamId <= 63)
            {
                buffer = _arrayPool.Rent(1);
                buffer[0] = (byte)((byte)(fmt << 6) | chunkStreamId);
                length = 1;
            }
            else if (chunkStreamId >= 64 && chunkStreamId <= 319)
            {
                buffer = _arrayPool.Rent(2);
                buffer[0] = (byte)(fmt << 6);
                buffer[1] = (byte)(chunkStreamId - 64);
                length = 2;
            }
            else if (chunkStreamId >= 320 && chunkStreamId <= 65599)
            {
                buffer = _arrayPool.Rent(3);
                buffer[0] = (byte)((fmt << 6) | 1);
                buffer[1] = (byte)((chunkStreamId - 64) & 0xff);
                buffer[2] = (byte)((chunkStreamId - 64) >> 8);
                length = 3;
            }
            else
            {
                throw new NotSupportedException();
            }
        }

        private ChunkHeaderType SelectChunkType(MessageHeader messageHeader, MessageHeader prevHeader)
        {
            if (prevHeader == null)
            {
                return ChunkHeaderType.Type0;
            }

            if (messageHeader.Timestamp == prevHeader.Timestamp &&
                messageHeader.MessageType == prevHeader.MessageType &&
                messageHeader.MessageLength == prevHeader.MessageLength &&
                messageHeader.MessageStreamId == prevHeader.MessageStreamId)
            {
                return ChunkHeaderType.Type3;
            }
            else if (messageHeader.MessageType == prevHeader.MessageType &&
                messageHeader.MessageLength == prevHeader.MessageLength &&
                messageHeader.MessageStreamId == prevHeader.MessageStreamId)
            {
                return ChunkHeaderType.Type2;
            }
            else if (messageHeader.MessageStreamId == prevHeader.MessageStreamId)
            {
                return ChunkHeaderType.Type1;
            }
            else
            {
                return ChunkHeaderType.Type0;
            }
        }
        #endregion

        #region Demultiplexing
        private bool ProcessHandshakeC0C1(ReadOnlySequence<byte> buffer, ref int consumed)
        {
            if (buffer.Length - consumed < 1537)
            {
                return false;
            }
            var arr = _arrayPool.Rent(1537);

            buffer.Slice(consumed, 1537).CopyTo(arr);
            consumed += 1537;
            var version = arr[0];
            _arrayPool.Return(arr);

            if (version < 3)
            {
                throw new NotSupportedException();
            }
            if (version > 31)
            {
                throw new ProtocolViolationException();
            }

            _readerTimestampEpoch = NetworkBitConverter.ToUInt32(arr.AsSpan(1, 4));
            _writerTimestampEpoch = 0;
            var allZero = arr.AsSpan(5, 4);
            if (allZero[0] != 0 || allZero[1] != 0 || allZero[2] != 0 || allZero[3] != 0)
            {
                throw new ProtocolViolationException();
            }
            _c1Data = _arrayPool.Rent(1528);

            arr.AsSpan(9).CopyTo(_c1Data);
            _s1Data = _arrayPool.Rent(1528);
            _random.NextBytes(_s1Data.AsSpan(1528));

            arr.AsSpan().Clear();
            arr[0] = 3;
            NetworkBitConverter.TryGetBytes(_writerTimestampEpoch, arr.AsSpan(1, 4));
            _s1Data.AsSpan(0, 1528).CopyTo(arr.AsSpan(9));
            SendRawData(arr, 1537);

            _nextProcessState = ProcessState.HandshakeC2;
            return true;



        }

        private bool ProcessHandshakeC2(ReadOnlySequence<byte> buffer, ref int consumed)
        {
            if (buffer.Length - consumed < 1536)
            {
                return false;
            }
            byte[] arr = _arrayPool.Rent(1536);
            try
            {
                buffer.Slice(consumed, 1536).CopyTo(arr);
                consumed += 1536;
                var s1Timestamp = NetworkBitConverter.ToUInt32(arr.AsSpan(0, 4));
                if (s1Timestamp != _writerTimestampEpoch)
                {
                    throw new ProtocolViolationException();
                }

                if (!arr.AsSpan(8, 1528).SequenceEqual(_s1Data))
                {
                    throw new ProtocolViolationException();
                }

                NetworkBitConverter.TryGetBytes(_readerTimestampEpoch, arr.AsSpan(0, 4));
                NetworkBitConverter.TryGetBytes((uint)0, arr.AsSpan(4, 4));
                _c1Data.AsSpan(0, 1528).CopyTo(arr.AsSpan(8));
                SendRawData(arr, 1536);
                OnHandshakeSuccessful();
                _nextProcessState = ProcessState.FirstByteBasicHeader;
                return true;
            }
            finally
            {
                _arrayPool.Return(_c1Data);
                _arrayPool.Return(_s1Data);
                _s1Data = null;
            }

        }

        private void FillHeader(ChunkHeader header)
        {
            if (!_previousReadMessageHeader.TryGetValue(header.ChunkBasicHeader.ChunkStreamId, out var prevHeader) &&
                header.ChunkBasicHeader.RtmpChunkHeaderType != ChunkHeaderType.Type0)
            {
                throw new InvalidOperationException();
            }

            switch (header.ChunkBasicHeader.RtmpChunkHeaderType)
            {
                case ChunkHeaderType.Type1:
                    header.MessageHeader.MessageStreamId = prevHeader.MessageStreamId;
                    break;
                case ChunkHeaderType.Type2:
                    header.MessageHeader.MessageLength = prevHeader.MessageLength;
                    header.MessageHeader.MessageType = prevHeader.MessageType;
                    header.MessageHeader.MessageStreamId = prevHeader.MessageStreamId;
                    break;
                case ChunkHeaderType.Type3:
                    header.MessageHeader.Timestamp = prevHeader.Timestamp;
                    header.MessageHeader.MessageLength = prevHeader.MessageLength;
                    header.MessageHeader.MessageType = prevHeader.MessageType;
                    header.MessageHeader.MessageStreamId = prevHeader.MessageStreamId;
                    break;
            }
        }

        private bool ProcessFirstByteBasicHeader(ReadOnlySequence<byte> buffer, ref int consumed)
        {
            if (buffer.Length - consumed < 1)
            {
                return false;
            }

            var header = new ChunkHeader()
            {
                ChunkBasicHeader = new ChunkBasicHeader(),
                MessageHeader = new MessageHeader()
            };
            _processingChunk = header;
            var arr = _arrayPool.Rent(1);
            buffer.Slice(consumed, 1).CopyTo(arr);
            consumed += 1;
            var basicHeader = arr[0];
            _arrayPool.Return(arr);
            header.ChunkBasicHeader.RtmpChunkHeaderType = (ChunkHeaderType)(basicHeader >> 6);
            header.ChunkBasicHeader.ChunkStreamId = (uint)basicHeader & 0x00FFFFFF;
            if (header.ChunkBasicHeader.ChunkStreamId != 0 && header.ChunkBasicHeader.ChunkStreamId != 0x00FFFFFF)
            {
                if (header.ChunkBasicHeader.RtmpChunkHeaderType == ChunkHeaderType.Type3)
                {
                    FillHeader(header);
                    _nextProcessState = ProcessState.CompleteMessage;
                }
            }
            else
            {
                _nextProcessState = ProcessState.ChunkMessageHeader;
            }
            return true;
        }

        private bool ProcessChunkMessageHeader(ReadOnlySequence<byte> buffer, ref int consumed)
        {
            int bytesNeed = 0;
            switch (_processingChunk.ChunkBasicHeader.ChunkStreamId)
            {
                case 0:
                    bytesNeed = 1;
                    break;
                case 0x00FFFFFF:
                    bytesNeed = 2;
                    break;
            }
            switch (_processingChunk.ChunkBasicHeader.RtmpChunkHeaderType)
            {
                case ChunkHeaderType.Type0:
                    bytesNeed += TYPE0_SIZE;
                    break;
                case ChunkHeaderType.Type1:
                    bytesNeed += TYPE1_SIZE;
                    break;
                case ChunkHeaderType.Type2:
                    bytesNeed += TYPE2_SIZE;
                    break;
            }

            if (buffer.Length - consumed <= bytesNeed)
            {
                return false;
            }

            byte[] arr = null;
            if (_processingChunk.ChunkBasicHeader.ChunkStreamId == 0)
            {
                arr = _arrayPool.Rent(1);
                buffer.Slice(consumed, 1).CopyTo(arr);
                consumed += 1;
                _processingChunk.ChunkBasicHeader.ChunkStreamId = (uint)arr[0] + 64;
                _arrayPool.Return(arr);
            }
            else if (_processingChunk.ChunkBasicHeader.ChunkStreamId == 0x00FFFFFF)
            {
                arr = _arrayPool.Rent(2);
                buffer.Slice(consumed, 2).CopyTo(arr);
                consumed += 2;
                _processingChunk.ChunkBasicHeader.ChunkStreamId = (uint)arr[1] * 256 + arr[0] + 64;
                _arrayPool.Return(arr);
            }
            var header = _processingChunk;
            switch (header.ChunkBasicHeader.RtmpChunkHeaderType)
            {
                case ChunkHeaderType.Type0:
                    arr = _arrayPool.Rent(TYPE0_SIZE);
                    buffer.Slice(consumed, TYPE0_SIZE).CopyTo(arr);
                    consumed += TYPE0_SIZE;
                    header.MessageHeader.Timestamp = NetworkBitConverter.ToUInt24(arr.AsSpan(0, 3));
                    header.MessageHeader.MessageLength = NetworkBitConverter.ToUInt24(arr.AsSpan(3, 3));
                    header.MessageHeader.MessageType = (MessageType)NetworkBitConverter.ToUInt24(arr.AsSpan(6, 1));
                    header.MessageHeader.MessageStreamId = NetworkBitConverter.ToUInt32(arr.AsSpan(7, 4), true);
                    break;
                case ChunkHeaderType.Type1:
                    arr = _arrayPool.Rent(TYPE1_SIZE);
                    buffer.Slice(consumed, TYPE1_SIZE).CopyTo(arr);
                    consumed += TYPE1_SIZE;
                    header.MessageHeader.Timestamp = NetworkBitConverter.ToUInt24(arr.AsSpan(0, 3));
                    header.MessageHeader.MessageLength = NetworkBitConverter.ToUInt24(arr.AsSpan(3, 3));
                    header.MessageHeader.MessageType = (MessageType)NetworkBitConverter.ToUInt24(arr.AsSpan(6, 1));
                    break;
                case ChunkHeaderType.Type2:
                    arr = _arrayPool.Rent(TYPE2_SIZE);
                    buffer.Slice(consumed, TYPE2_SIZE).CopyTo(arr);
                    consumed += TYPE2_SIZE;
                    header.MessageHeader.Timestamp = NetworkBitConverter.ToUInt24(arr.AsSpan(0, 3));
                    break;
            }
            if (arr != null)
            {
                _arrayPool.Return(arr);
            }
            FillHeader(header);
            if (header.MessageHeader.Timestamp == 0x00FFFFFF)
            {
                _nextProcessState = ProcessState.ExtendedTimestamp;
            }
            else
            {
                _nextProcessState = ProcessState.CompleteMessage;
            }
            return true;
        }

        private bool ProcessExtendedTimestamp(ReadOnlySequence<byte> buffer, ref int consumed)
        {
            if (buffer.Length - consumed < 4)
            {
                return false;
            }
            var arr = _arrayPool.Rent(4);
            buffer.Slice(consumed, 4).CopyTo(arr);
            var extendedTimestamp = NetworkBitConverter.ToUInt32(arr.AsSpan(0, 4));
            _processingChunk.ExtendedTimestamp = extendedTimestamp;
            _processingChunk.MessageHeader.Timestamp = extendedTimestamp;
            _nextProcessState = ProcessState.CompleteMessage;
            return true;
        }

        private bool ProcessCompleteMessage(ReadOnlySequence<byte> buffer, ref int consumed)
        {
            var header = _processingChunk;
            if (!_incompleteMessageState.TryGetValue(header.ChunkBasicHeader.ChunkStreamId, out var state))
            {
                state = new MessageReadingState()
                {
                    CurrentIndex = 0,
                    MessageLength = header.MessageHeader.MessageLength,
                    Body = _arrayPool.Rent((int)header.MessageHeader.MessageLength)
                };
                _incompleteMessageState.Add(header.ChunkBasicHeader.ChunkStreamId, state);
            }

            var bytesNeed = (int)(state.RemainBytes >= ReadChunkSize ? ReadChunkSize : state.RemainBytes);

            if (buffer.Length - consumed < bytesNeed)
            {
                return false;
            }

            if (_previousReadMessageHeader.TryGetValue(header.ChunkBasicHeader.ChunkStreamId, out var prevHeader))
            {
                if (prevHeader.MessageStreamId != header.MessageHeader.MessageStreamId)
                {
                    // inform user previous message will never be received
                    prevHeader = null;
                }
            }
            _previousReadMessageHeader[_processingChunk.ChunkBasicHeader.ChunkStreamId] = _processingChunk.MessageHeader;
            _processingChunk = null;

            buffer.Slice(consumed, bytesNeed).CopyTo(state.Body.AsSpan(state.CurrentIndex));
            consumed += bytesNeed;
            state.CurrentIndex = state.CurrentIndex + bytesNeed;

            if (state.IsCompleted)
            {
                _incompleteMessageState.Remove(header.ChunkBasicHeader.ChunkStreamId);
                try
                {
                    if (_messageIO.TryGetValue(header.MessageHeader.MessageType, out var reader))
                    {
                        var message = reader.ParseMessage(header.MessageHeader, state.Body);
                        _rtmpSession?.MessageArrived(message);
                    }
                }
                finally
                {
                    _arrayPool.Return(state.Body);
                }
            }
            _nextProcessState = ProcessState.FirstByteBasicHeader;
            return true;
        }
        #endregion


        #region IDisposable Support
        private bool disposedValue = false; // 要检测冗余调用

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _rtmpSession?.Dispose();
                    _socket.Dispose();
                }

                // TODO: 释放未托管的资源(未托管的对象)并在以下内容中替代终结器。
                // TODO: 将大型字段设置为 null。

                disposedValue = true;
            }
        }

        // TODO: 仅当以上 Dispose(bool disposing) 拥有用于释放未托管资源的代码时才替代终结器。
        // ~RtmpStream() {
        //   // 请勿更改此代码。将清理代码放入以上 Dispose(bool disposing) 中。
        //   Dispose(false);
        // }

        // 添加此代码以正确实现可处置模式。
        public void Dispose()
        {
            // 请勿更改此代码。将清理代码放入以上 Dispose(bool disposing) 中。
            Dispose(true);
            // TODO: 如果在以上内容中替代了终结器，则取消注释以下行。
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
