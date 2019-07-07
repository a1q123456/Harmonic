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
using Harmonic.Networking.Utils;
using Harmonic.Networking.Rtmp.Serialization;
using Harmonic.Buffers;
using Harmonic.Networking.Amf.Serialization.Amf0;
using Harmonic.Networking.Amf.Serialization.Amf3;
using System.Reflection;
using Harmonic.Networking.Rtmp.Messages.UserControlMessages;
using Harmonic.Networking.Rtmp.Messages.Commands;

namespace Harmonic.Networking.Rtmp
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

    // TBD: retransfer bytes when acknowledgement not received
    class IOPipeLine : IDisposable
    {

        internal delegate bool BufferProcessor(ReadOnlySequence<byte> buffer, ref int consumed);
        internal SemaphoreSlim _writerSignal = new SemaphoreSlim(0);

        private Socket _socket;
        private ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
        private MemoryPool<byte> _memoryPool = MemoryPool<byte>.Shared;
        internal ProcessState _nextProcessState = ProcessState.FirstByteBasicHeader;
        private readonly int _resumeWriterThreshole;
        internal Dictionary<ProcessState, BufferProcessor> _bufferProcessors;

        internal Queue<WriteState> _writerQueue = new Queue<WriteState>();
        internal ChunkStreamContext ChunkStreamContext { get; set; } = null;
        private HandshakeContext _handshakeContext = null;
        internal RtmpServerOptions _options = null;

        public IOPipeLine(Socket socket, RtmpServerOptions options, int resumeWriterThreshole = 65535)
        {
            _socket = socket;
            _resumeWriterThreshole = resumeWriterThreshole;
            _bufferProcessors = new Dictionary<ProcessState, BufferProcessor>();
            _options = options;
            _handshakeContext = new HandshakeContext(this);
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
                ChunkStreamContext?.Dispose();
                ChunkStreamContext = null;
            });
            return Task.WhenAll(t1, t2, t3);
        }

        internal void OnHandshakeSuccessful()
        {
            _handshakeContext = null;
            _bufferProcessors.Clear();
            ChunkStreamContext = new ChunkStreamContext(this);
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
                var memory = writer.GetMemory(ChunkStreamContext == null ? 1536 : ChunkStreamContext.ReadMinimumBufferSize);
                var bytesRead = await s.ReceiveAsync(memory, SocketFlags.None);
                if (bytesRead == 0)
                {
                    break;
                }
                if (ChunkStreamContext != null)
                {
                    ChunkStreamContext.ReadWindowSize += (uint)bytesRead;
                    if (ChunkStreamContext.ReadWindowAcknowledgementSize.HasValue)
                    {
                        if (ChunkStreamContext.ReadWindowSize >= ChunkStreamContext.ReadWindowAcknowledgementSize)
                        {
                            ChunkStreamContext._rtmpSession.Acknowledgement(ChunkStreamContext.ReadWindowAcknowledgementSize.Value);
                            ChunkStreamContext.ReadWindowSize -= ChunkStreamContext.ReadWindowAcknowledgementSize.Value;
                        }
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

        internal void Disconnect()
        {
            _socket.Close();
        }
        #endregion

        #region Multiplexing
        internal Task SendRawData(byte[] data, int length)
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
            return ChunkStreamContext?.MultiplexMessageAsync(chunkStreamId, message);
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
                    ChunkStreamContext?.Dispose();
                    _socket.Dispose();
                }

                // TODO: 释放未托管的资源(未托管的对象)并在以下内容中替代终结器。
                // TODO: 将大型字段设置为 null。

                disposedValue = true;
            }
        }

        // TODO: 仅当以上 Dispose(bool disposing) 拥有用于释放未托管资源的代码时才替代终结器。
        // ~IOPipeline() {
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
