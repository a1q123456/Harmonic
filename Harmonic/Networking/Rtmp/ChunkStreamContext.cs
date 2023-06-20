using Harmonic.Buffers;
using Harmonic.Networking.Amf.Serialization.Amf0;
using Harmonic.Networking.Amf.Serialization.Amf3;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Messages;
using Harmonic.Networking.Rtmp.Serialization;
using Harmonic.Networking.Utils;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Harmonic.Networking.Rtmp;

class ChunkStreamContext : IDisposable
{
    private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
    internal ChunkHeader _processingChunk;
    internal int ReadMinimumBufferSize { get => (ReadChunkSize + _type0_size) * 4; }
    internal Dictionary<uint, MessageHeader> _previousWriteMessageHeader = new();
    internal Dictionary<uint, MessageHeader> _previousReadMessageHeader = new();
    internal Dictionary<uint, MessageReadingState> _incompleteMessageState = new();
    internal uint? ReadWindowAcknowledgementSize { get; set; }
    internal uint? WriteWindowAcknowledgementSize { get; set; }
    internal int ReadChunkSize { get; set; } = 128;
    internal long _readUnAcknowledgedSize = 0;
    internal long _writeUnAcknowledgedSize;

    internal uint _writeChunkSize = 128;
    internal readonly int _extended_timestamp_length = 4;
    internal readonly int _type0_size = 11;
    internal readonly int _type1_size = 7;
    internal readonly int _type2_size = 3;

    internal RtmpSession _rtmpSession;

    internal Amf0Reader _amf0Reader = new();
    internal Amf0Writer _amf0Writer = new();
    internal Amf3Reader _amf3Reader = new();
    internal Amf3Writer _amf3Writer = new();


    private readonly IoPipeLine _ioPipeline;
    private readonly SemaphoreSlim _sync = new(1);
    internal LimitType? PreviousLimitType { get; set; } = null;

    public ChunkStreamContext(IoPipeLine stream)
    {
        _rtmpSession = new RtmpSession(stream);
        _ioPipeline = stream;
        _ioPipeline.NextProcessState = ProcessState.FirstByteBasicHeader;
        _ioPipeline._bufferProcessors.Add(ProcessState.ChunkMessageHeader, ProcessChunkMessageHeader);
        _ioPipeline._bufferProcessors.Add(ProcessState.CompleteMessage, ProcessCompleteMessage);
        _ioPipeline._bufferProcessors.Add(ProcessState.ExtendedTimestamp, ProcessExtendedTimestamp);
        _ioPipeline._bufferProcessors.Add(ProcessState.FirstByteBasicHeader, ProcessFirstByteBasicHeader);
    }

    public void Dispose()
    {
        ((IDisposable)_rtmpSession).Dispose();
    }

    internal async Task MultiplexMessageAsync(uint chunkStreamId, Message message)
    {
        if (!message.MessageHeader.MessageStreamId.HasValue)
        {
            throw new InvalidOperationException("cannot send message that has not attached to a message stream");
        }
        byte[] buffer = null;
        uint length = 0;
        using (var writeBuffer = new ByteBuffer())
        {
            var context = new Serialization.SerializationContext()
            {
                Amf0Reader = _amf0Reader,
                Amf0Writer = _amf0Writer,
                Amf3Reader = _amf3Reader,
                Amf3Writer = _amf3Writer,
                WriteBuffer = writeBuffer
            };
            message.Serialize(context);
            length = (uint)writeBuffer.Length;
            Debug.Assert(length != 0);
            buffer = _arrayPool.Rent((int)length);
            writeBuffer.TakeOutMemory(buffer);
        }

        try
        {
            message.MessageHeader.MessageLength = length;
            Debug.Assert(message.MessageHeader.MessageLength != 0);
            if (message.MessageHeader.MessageType == 0)
            {
                message.MessageHeader.MessageType = message.GetType().GetCustomAttribute<RtmpMessageAttribute>().MessageTypes.First();
            }
            Debug.Assert(message.MessageHeader.MessageType != 0);
            Task ret = null;
            // chunking
            bool isFirstChunk = true;
            _rtmpSession.AssertStreamId(message.MessageHeader.MessageStreamId.Value);
            for (int i = 0; i < message.MessageHeader.MessageLength;)
            {
                _previousWriteMessageHeader.TryGetValue(chunkStreamId, out var prevHeader);
                var chunkHeaderType = SelectChunkType(message.MessageHeader, prevHeader, isFirstChunk);
                isFirstChunk = false;
                GenerateBasicHeader(chunkHeaderType, chunkStreamId, out var basicHeader, out var basicHeaderLength);
                GenerateMesesageHeader(chunkHeaderType, message.MessageHeader, prevHeader, out var messageHeader, out var messageHeaderLength);
                _previousWriteMessageHeader[chunkStreamId] = (MessageHeader)message.MessageHeader.Clone();
                var headerLength = basicHeaderLength + messageHeaderLength;
                var bodySize = (int)(length - i >= _writeChunkSize ? _writeChunkSize : length - i);

                var chunkBuffer = _arrayPool.Rent(headerLength + bodySize);
                await _sync.WaitAsync();
                try
                {
                    basicHeader.AsSpan(0, basicHeaderLength).CopyTo(chunkBuffer);
                    messageHeader.AsSpan(0, messageHeaderLength).CopyTo(chunkBuffer.AsSpan(basicHeaderLength));
                    _arrayPool.Return(basicHeader);
                    _arrayPool.Return(messageHeader);
                    buffer.AsSpan(i, bodySize).CopyTo(chunkBuffer.AsSpan(headerLength));
                    i += bodySize;
                    var isLastChunk = message.MessageHeader.MessageLength - i == 0;

                    long offset = 0;
                    long totalLength = headerLength + bodySize;
                    long currentSendSize = totalLength;

                    while (offset != (headerLength + bodySize))
                    {
                        if (WriteWindowAcknowledgementSize.HasValue && Interlocked.Read(ref _writeUnAcknowledgedSize) + headerLength + bodySize > WriteWindowAcknowledgementSize.Value)
                        {
                            currentSendSize = Math.Min(WriteWindowAcknowledgementSize.Value, currentSendSize);
                            //var delayCount = 0;
                            while (currentSendSize + Interlocked.Read(ref _writeUnAcknowledgedSize) >= WriteWindowAcknowledgementSize.Value)
                            {
                                await Task.Delay(1);
                            }
                        }
                        var tsk = _ioPipeline.SendRawData(chunkBuffer.AsMemory((int)offset, (int)currentSendSize));
                        offset += currentSendSize;
                        totalLength -= currentSendSize;

                        if (WriteWindowAcknowledgementSize.HasValue)
                        {
                            Interlocked.Add(ref _writeUnAcknowledgedSize, currentSendSize);
                        }
                            
                        if (isLastChunk)
                        {
                            ret = tsk;
                        }
                    }
                    if (isLastChunk)
                    {
                        if (message.MessageHeader.MessageType == MessageType.SetChunkSize)
                        {
                            var setChunkSize = message as SetChunkSizeMessage;
                            _writeChunkSize = setChunkSize.ChunkSize;
                        }
                        else if (message.MessageHeader.MessageType == MessageType.SetPeerBandwidth)
                        {
                            var m = message as SetPeerBandwidthMessage;
                            ReadWindowAcknowledgementSize = m.WindowSize;
                        }
                        else if (message.MessageHeader.MessageType == MessageType.WindowAcknowledgementSize)
                        {
                            var m = message as WindowAcknowledgementSizeMessage;
                            WriteWindowAcknowledgementSize = m.WindowSize;
                        }
                    }
                }
                finally
                {
                    _sync.Release();
                    _arrayPool.Return(chunkBuffer);
                }
            }
            Debug.Assert(ret != null);
            await ret;

        }
        finally
        {
            _arrayPool.Return(buffer);
        }

    }

    private void GenerateMesesageHeader(ChunkHeaderType chunkHeaderType, MessageHeader header, MessageHeader prevHeader, out byte[] buffer, out int length)
    {
        var timestamp = header.Timestamp;
        switch (chunkHeaderType)
        {
            case ChunkHeaderType.Type0:
                buffer = _arrayPool.Rent(_type0_size + _extended_timestamp_length);
                NetworkBitConverter.TryGetUInt24Bytes(timestamp >= 0xFFFFFF ? 0xFFFFFF : timestamp, buffer.AsSpan(0, 3));
                NetworkBitConverter.TryGetUInt24Bytes(header.MessageLength, buffer.AsSpan(3, 3));
                NetworkBitConverter.TryGetBytes((byte)header.MessageType, buffer.AsSpan(6, 1));
                NetworkBitConverter.TryGetBytes(header.MessageStreamId.Value, buffer.AsSpan(7, 4), true);
                length = _type0_size;
                break;
            case ChunkHeaderType.Type1:
                buffer = _arrayPool.Rent(_type1_size + _extended_timestamp_length);
                timestamp = timestamp - prevHeader.Timestamp;
                NetworkBitConverter.TryGetUInt24Bytes(timestamp >= 0xFFFFFF ? 0xFFFFFF : timestamp, buffer.AsSpan(0, 3));
                NetworkBitConverter.TryGetUInt24Bytes(header.MessageLength, buffer.AsSpan(3, 3));
                NetworkBitConverter.TryGetBytes((byte)header.MessageType, buffer.AsSpan(6, 1));
                length = _type1_size;
                break;
            case ChunkHeaderType.Type2:
                buffer = _arrayPool.Rent(_type2_size + _extended_timestamp_length);
                timestamp = timestamp - prevHeader.Timestamp;
                NetworkBitConverter.TryGetUInt24Bytes(timestamp >= 0xFFFFFF ? 0xFFFFFF : timestamp, buffer.AsSpan(0, 3));
                length = _type2_size;
                break;
            case ChunkHeaderType.Type3:
                buffer = _arrayPool.Rent(_extended_timestamp_length);
                length = 0;
                break;
            default:
                throw new ArgumentException();
        }
        if (timestamp >= 0xFFFFFF)
        {
            NetworkBitConverter.TryGetBytes(timestamp, buffer.AsSpan(length, _extended_timestamp_length));
            length += _extended_timestamp_length;
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

    private ChunkHeaderType SelectChunkType(MessageHeader messageHeader, MessageHeader prevHeader, bool isFirstChunk)
    {
        if (prevHeader == null)
        {
            return ChunkHeaderType.Type0;
        }

        if (!isFirstChunk)
        {
            return ChunkHeaderType.Type3;
        }

        long currentTimestamp = messageHeader.Timestamp;
        long prevTimesatmp = prevHeader.Timestamp;

        if (currentTimestamp - prevTimesatmp < 0)
        {
            return ChunkHeaderType.Type0;
        }

        if (messageHeader.MessageType == prevHeader.MessageType &&
            messageHeader.MessageLength == prevHeader.MessageLength &&
            messageHeader.MessageStreamId == prevHeader.MessageStreamId &&
            messageHeader.Timestamp != prevHeader.Timestamp)
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
                header.MessageHeader.Timestamp += prevHeader.Timestamp;
                header.MessageHeader.MessageStreamId = prevHeader.MessageStreamId;
                break;
            case ChunkHeaderType.Type2:
                header.MessageHeader.Timestamp += prevHeader.Timestamp;
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


    public bool ProcessFirstByteBasicHeader(ReadOnlySequence<byte> buffer, ref int consumed)
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
        header.ChunkBasicHeader.ChunkStreamId = (uint)basicHeader & 0x3F;
        if (header.ChunkBasicHeader.ChunkStreamId != 0 && header.ChunkBasicHeader.ChunkStreamId != 0x3F)
        {
            if (header.ChunkBasicHeader.RtmpChunkHeaderType == ChunkHeaderType.Type3)
            {
                FillHeader(header);
                _ioPipeline.NextProcessState = ProcessState.CompleteMessage;
                return true;
            }
        }
        _ioPipeline.NextProcessState = ProcessState.ChunkMessageHeader;
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
            case 0x3F:
                bytesNeed = 2;
                break;
        }
        switch (_processingChunk.ChunkBasicHeader.RtmpChunkHeaderType)
        {
            case ChunkHeaderType.Type0:
                bytesNeed += _type0_size;
                break;
            case ChunkHeaderType.Type1:
                bytesNeed += _type1_size;
                break;
            case ChunkHeaderType.Type2:
                bytesNeed += _type2_size;
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
        else if (_processingChunk.ChunkBasicHeader.ChunkStreamId == 0x3F)
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
                arr = _arrayPool.Rent(_type0_size);
                buffer.Slice(consumed, _type0_size).CopyTo(arr);
                consumed += _type0_size;
                header.MessageHeader.Timestamp = NetworkBitConverter.ToUInt24(arr.AsSpan(0, 3));
                header.MessageHeader.MessageLength = NetworkBitConverter.ToUInt24(arr.AsSpan(3, 3));
                header.MessageHeader.MessageType = (MessageType)arr[6];
                header.MessageHeader.MessageStreamId = NetworkBitConverter.ToUInt32(arr.AsSpan(7, 4), true);
                break;
            case ChunkHeaderType.Type1:
                arr = _arrayPool.Rent(_type1_size);
                buffer.Slice(consumed, _type1_size).CopyTo(arr);
                consumed += _type1_size;
                header.MessageHeader.Timestamp = NetworkBitConverter.ToUInt24(arr.AsSpan(0, 3));
                header.MessageHeader.MessageLength = NetworkBitConverter.ToUInt24(arr.AsSpan(3, 3));
                header.MessageHeader.MessageType = (MessageType)arr[6];
                break;
            case ChunkHeaderType.Type2:
                arr = _arrayPool.Rent(_type2_size);
                buffer.Slice(consumed, _type2_size).CopyTo(arr);
                consumed += _type2_size;
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
            _ioPipeline.NextProcessState = ProcessState.ExtendedTimestamp;
        }
        else
        {
            _ioPipeline.NextProcessState = ProcessState.CompleteMessage;
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
        consumed += 4;
        var extendedTimestamp = NetworkBitConverter.ToUInt32(arr.AsSpan(0, 4));
        _processingChunk.ExtendedTimestamp = extendedTimestamp;
        _processingChunk.MessageHeader.Timestamp = extendedTimestamp;
        _ioPipeline.NextProcessState = ProcessState.CompleteMessage;
        return true;
    }

    private bool ProcessCompleteMessage(ReadOnlySequence<byte> buffer, ref int consumed)
    {
        var header = _processingChunk;
        if (!_incompleteMessageState.TryGetValue(header.ChunkBasicHeader.ChunkStreamId, out var state))
        {
            state = new MessageReadingState()
            {
                _currentIndex = 0,
                _messageLength = header.MessageHeader.MessageLength,
                _body = _arrayPool.Rent((int)header.MessageHeader.MessageLength)
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
        _previousReadMessageHeader[_processingChunk.ChunkBasicHeader.ChunkStreamId] = (MessageHeader)_processingChunk.MessageHeader.Clone();
        _processingChunk = null;

        buffer.Slice(consumed, bytesNeed).CopyTo(state._body.AsSpan(state._currentIndex));
        consumed += bytesNeed;
        state._currentIndex += bytesNeed;

        if (state.IsCompleted)
        {
            _incompleteMessageState.Remove(header.ChunkBasicHeader.ChunkStreamId);
            try
            {
                var context = new Serialization.SerializationContext()
                {
                    Amf0Reader = _amf0Reader,
                    Amf0Writer = _amf0Writer,
                    Amf3Reader = _amf3Reader,
                    Amf3Writer = _amf3Writer,
                    ReadBuffer = state._body.AsMemory(0, (int)state._messageLength)
                };
                if (header.MessageHeader.MessageType == MessageType.AggregateMessage)
                {
                    var agg = new AggregateMessage()
                    {
                        MessageHeader = header.MessageHeader
                    };
                    agg.Deserialize(context);
                    foreach (var message in agg.Messages)
                    {
                        if (!_ioPipeline.Options.MessageFactories.TryGetValue(message.Header.MessageType, out var factory))
                        {
                            continue;
                        }
                        var msgContext = new Serialization.SerializationContext()
                        {
                            Amf0Reader = context.Amf0Reader,
                            Amf3Reader = context.Amf3Reader,
                            Amf0Writer = context.Amf0Writer,
                            Amf3Writer = context.Amf3Writer,
                            ReadBuffer = context.ReadBuffer.Slice(message.DataOffset, (int)message.DataLength)
                        };
                        try
                        {
                            var msg = factory(header.MessageHeader, msgContext, out var factoryConsumed);
                            msg.MessageHeader = header.MessageHeader;
                            msg.Deserialize(msgContext);
                            context.Amf0Reader.ResetReference();
                            context.Amf3Reader.ResetReference();
                            _rtmpSession.MessageArrived(msg);
                        }
                        catch (NotSupportedException)
                        {

                        }
                    }
                }
                else
                {
                    if (_ioPipeline.Options._messageFactories.TryGetValue(header.MessageHeader.MessageType, out var factory))
                    {
                        try
                        {
                            var message = factory(header.MessageHeader, context, out var factoryConsumed);
                            message.MessageHeader = header.MessageHeader;
                            context.ReadBuffer = context.ReadBuffer.Slice(factoryConsumed);
                            message.Deserialize(context);
                            context.Amf0Reader.ResetReference();
                            context.Amf3Reader.ResetReference();
                            _rtmpSession.MessageArrived(message);
                        }
                        catch (NotSupportedException)
                        {

                        }
                    }
                }
            }
            finally
            {
                _arrayPool.Return(state._body);
            }
        }
        _ioPipeline.NextProcessState = ProcessState.FirstByteBasicHeader;
        return true;
    }

}