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
using Harmonic.Hosting;
using System.Linq;
using System.Diagnostics;

namespace Harmonic.Networking.Rtmp
{
    enum ProcessState
    {
        HandshakeC0C1,
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
        private SemaphoreSlim _writerSignal = new SemaphoreSlim(0);

        private Socket _socket;
        private ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
        private readonly int _resumeWriterThreshole;
        internal Dictionary<ProcessState, BufferProcessor> _bufferProcessors;

        private ConcurrentQueue<WriteState> _writerQueue = new ConcurrentQueue<WriteState>();

        internal ProcessState NextProcessState { get; set; } = ProcessState.HandshakeC0C1;
        internal ChunkStreamContext ChunkStreamContext { get; set; } = null;
        private HandshakeContext _handshakeContext = null;
        public RtmpServerOptions Options { get; set; } = null;


        public IOPipeLine(Socket socket, RtmpServerOptions options, int resumeWriterThreshole = 65535)
        {
            _socket = socket;
            _resumeWriterThreshole = resumeWriterThreshole;
            _bufferProcessors = new Dictionary<ProcessState, BufferProcessor>();
            Options = options;
            _handshakeContext = new HandshakeContext(this);

        }

        public Task StartAsync(CancellationToken ct = default)
        {
            var d = PipeOptions.Default;
            var opt = new PipeOptions(
                d.Pool,
                d.ReaderScheduler,
                d.WriterScheduler,
                _resumeWriterThreshole,
                d.ResumeWriterThreshold,
                d.MinimumSegmentSize,
                d.UseSynchronizationContext);
            var pipe = new Pipe(opt);
            var t1 = Producer(_socket, pipe.Writer, ct);
            var t2 = Consumer(pipe.Reader, ct);
            var t3 = Writer(ct);
            ct.Register(() =>
            {
                ChunkStreamContext?.Dispose();
                ChunkStreamContext = null;
            });

            var tcs = new TaskCompletionSource<int>();
            Action<Task> setException = t =>
            {
                tcs.TrySetException(t.Exception.InnerException);
            };
            Action<Task> setCanceled = _ =>
            {
                tcs.TrySetCanceled();
            };
            Action<Task> setResult = t =>
            {
                tcs.TrySetResult(1);
            };


            t1.ContinueWith(setException, TaskContinuationOptions.OnlyOnFaulted);
            t2.ContinueWith(setException, TaskContinuationOptions.OnlyOnFaulted);
            t3.ContinueWith(setException, TaskContinuationOptions.OnlyOnFaulted);
            t1.ContinueWith(setCanceled, TaskContinuationOptions.OnlyOnCanceled);
            t2.ContinueWith(setCanceled, TaskContinuationOptions.OnlyOnCanceled);
            t3.ContinueWith(setCanceled, TaskContinuationOptions.OnlyOnCanceled);
            t1.ContinueWith(setResult, TaskContinuationOptions.OnlyOnRanToCompletion);
            t2.ContinueWith(setResult, TaskContinuationOptions.OnlyOnRanToCompletion);
            t3.ContinueWith(setResult, TaskContinuationOptions.OnlyOnRanToCompletion);
            return tcs.Task;
        }

        internal void OnHandshakeSuccessful()
        {
            _handshakeContext = null;
            _bufferProcessors.Clear();
            ChunkStreamContext = new ChunkStreamContext(this);
        }

        #region Sender

        internal async Task SendRawData(ReadOnlyMemory<byte> data)
        {
            var tcs = new TaskCompletionSource<int>();
            var buffer = _arrayPool.Rent(data.Length);
            data.CopyTo(buffer);

            _writerQueue.Enqueue(new WriteState()
            {
                Buffer = buffer,
                TaskSource = tcs,
                Length = data.Length
            });
            _writerSignal.Release();
            await tcs.Task;
        }

        private async Task Writer(CancellationToken ct)
        { 
            while (!ct.IsCancellationRequested && !disposedValue)
            {
                await _writerSignal.WaitAsync(ct);
                if (_writerQueue.TryDequeue(out var data))
                {
                    Debug.Assert(data != null);
                    Debug.Assert(_socket != null);
                    Debug.Assert((data.Buffer[0] & 0x3F) < 10);
                    await _socket.SendAsync(data.Buffer.AsMemory(0, data.Length), SocketFlags.None, ct);
                    _arrayPool.Return(data.Buffer);
                    data.TaskSource?.SetResult(1);
                }
                else
                {
                    Debug.Assert(false);
                }
            }
        }
        #endregion

        #region Receiver
        private async Task Producer(Socket s, PipeWriter writer, CancellationToken ct = default)
        {
            while (!ct.IsCancellationRequested && !disposedValue)
            {
                var memory = writer.GetMemory(ChunkStreamContext == null ? 1536 : ChunkStreamContext.ReadMinimumBufferSize);
                var bytesRead = await s.ReceiveAsync(memory, SocketFlags.None);
                if (bytesRead == 0)
                {
                    break;
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
            while (!ct.IsCancellationRequested && !disposedValue)
            {
                var result = await reader.ReadAsync(ct);
                var buffer = result.Buffer;
                int consumed = 0;

                while (true)
                {
                    if (!_bufferProcessors[NextProcessState](buffer, ref consumed))
                    {
                        break;
                    }
                }
                buffer = buffer.Slice(consumed);

                reader.AdvanceTo(buffer.Start, buffer.End);
                if (ChunkStreamContext != null)
                {
                    ChunkStreamContext.ReadUnAcknowledgedSize += consumed;
                    if (ChunkStreamContext.ReadWindowAcknowledgementSize.HasValue)
                    {
                        if (ChunkStreamContext.ReadUnAcknowledgedSize >= ChunkStreamContext.ReadWindowAcknowledgementSize)
                        {
                            ChunkStreamContext._rtmpSession.Acknowledgement((uint)ChunkStreamContext.ReadUnAcknowledgedSize);
                            ChunkStreamContext.ReadUnAcknowledgedSize -= 0;
                        }
                    }
                }
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
            Dispose();
        }
        #endregion



        #region IDisposable Support
        private bool disposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _handshakeContext?.Dispose();
                    ChunkStreamContext?.Dispose();
                    _socket.Dispose();
                    _writerSignal.Dispose();

                }


                disposedValue = true;
            }
        }

        // ~IOPipeline() {
        //   Dispose(false);
        // }

        public void Dispose()
        {
            Dispose(true);
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
