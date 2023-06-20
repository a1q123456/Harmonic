using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Harmonic.Buffers;

public class ByteBuffer : IDisposable
{
    private List<byte[]> _buffers = new List<byte[]>();
    private int _bufferEnd = 0;
    private int _bufferStart = 0;
    private readonly int _maxiumBufferSize = 0;
    private event Action _memoryUnderLimit;
    private event Action _dataWritten;
    private object _sync = new object();
    private ArrayPool<byte> _arrayPool;
    public int BufferSegmentSize { get; }
    public int Length
    {
        get
        {
            return _buffers.Count * BufferSegmentSize - BufferBytesAvailable() - _bufferStart;
        }
    }

    public ByteBuffer(int bufferSegmentSize = 1024, int maxiumBufferSize = -1, ArrayPool<byte> arrayPool = null)
    {
        if (bufferSegmentSize == 0)
        {
            throw new ArgumentOutOfRangeException();
        }

        BufferSegmentSize = bufferSegmentSize;
        _maxiumBufferSize = maxiumBufferSize;
        if (arrayPool != null)
        {
            _arrayPool = arrayPool;
        }
        else
        {
            _arrayPool = ArrayPool<byte>.Shared;
        }
        _buffers.Add(_arrayPool.Rent(bufferSegmentSize));
    }

    private int BufferBytesAvailable()
    {
        return BufferSegmentSize - _bufferEnd;
    }

    private void AddNewBufferSegment()
    {
        var arr = _arrayPool.Rent(BufferSegmentSize);
        Debug.Assert(_buffers.IndexOf(arr) == -1);
        _buffers.Add(arr);
        _bufferEnd = 0;
    }

    public void WriteToBuffer(byte data)
    {
        if (Length > _maxiumBufferSize && _maxiumBufferSize >= 0)
        {
            throw new InvalidOperationException("buffer length exceeded");
        }
        lock (_sync)
        {
            int available = BufferBytesAvailable();
            byte[] buffer = null;
            if (available == 0)
            {
                AddNewBufferSegment();
                buffer = _buffers.Last();
            }
            else
            {
                buffer = _buffers.Last();
            }
            buffer[_bufferEnd] = data;
            _bufferEnd += 1;
        }
    }

    private void WriteToBufferNoCheck(ReadOnlySpan<byte> bytes)
    {
        lock (_sync)
        {
            var requiredLength = bytes.Length;
            int available = BufferBytesAvailable();
            if (available < requiredLength)
            {
                var bytesIndex = 0;
                do
                {
                    var buffer = _buffers.Last();
                    var seq = bytes.Slice(bytesIndex, Math.Min(available, requiredLength));
                    seq.CopyTo(buffer.AsSpan(_bufferEnd));
                    _bufferEnd += seq.Length;
                    requiredLength -= seq.Length;
                    available -= seq.Length;
                    bytesIndex += seq.Length;

                    if (available == 0)
                    {
                        AddNewBufferSegment();
                        available = BufferBytesAvailable();
                    }
                }
                while (requiredLength != 0);
            }
            else
            {
                var buffer = _buffers.Last();
                bytes.CopyTo(buffer.AsSpan(_bufferEnd));
                _bufferEnd += bytes.Length;
            }
        }
        _dataWritten?.Invoke();
    }
    class _source : IValueTaskSource
    {
        private static readonly Action<object> CallbackCompleted = _ => { Debug.Assert(false, "Should not be invoked"); };

        private List<Action> cb = new List<Action>();
        private ValueTaskSourceStatus status = ValueTaskSourceStatus.Pending;
        private ExecutionContext executionContext;
        private object scheduler;
        private object state;
        private Action<object> continuation;

        public _source()
        {
        }

        public void Cancel()
        {
            status = ValueTaskSourceStatus.Canceled;
        }
        public void Success()
        {
            status = ValueTaskSourceStatus.Succeeded;
            var previousContinuation = Interlocked.CompareExchange(ref this.continuation, CallbackCompleted, null);
            if (previousContinuation != null)
            {
                // Async work completed, continue with... continuation
                ExecutionContext ec = executionContext;
                if (ec == null)
                {
                    InvokeContinuation(previousContinuation, this.state, forceAsync: false);
                }
                else
                {
                    // This case should be relatively rare, as the async Task/ValueTask method builders
                    // use the awaiter's UnsafeOnCompleted, so this will only happen with code that
                    // explicitly uses the awaiter's OnCompleted instead.
                    executionContext = null;
                    ExecutionContext.Run(ec, runState =>
                    {
                        var t = (Tuple<_source, Action<object>, object>)runState;
                        t.Item1.InvokeContinuation(t.Item2, t.Item3, forceAsync: false);
                    }, Tuple.Create(this, previousContinuation, this.state));
                }
            }
        }

        public void GetResult(short token)
        {
            return;
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            return status;
        }

        public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            if ((flags & ValueTaskSourceOnCompletedFlags.FlowExecutionContext) != 0)
            {
                this.executionContext = ExecutionContext.Capture();
            }

            if ((flags & ValueTaskSourceOnCompletedFlags.UseSchedulingContext) != 0)
            {
                SynchronizationContext sc = SynchronizationContext.Current;
                if (sc != null && sc.GetType() != typeof(SynchronizationContext))
                {
                    this.scheduler = sc;
                }
                else
                {
                    TaskScheduler ts = TaskScheduler.Current;
                    if (ts != TaskScheduler.Default)
                    {
                        this.scheduler = ts;
                    }
                }
            }

            // Remember current state
            this.state = state;
            // Remember continuation to be executed on completed (if not already completed, in case of which
            // continuation will be set to CallbackCompleted)
            var previousContinuation = Interlocked.CompareExchange(ref this.continuation, continuation, null);
            if (previousContinuation != null)
            {
                if (!ReferenceEquals(previousContinuation, CallbackCompleted))
                {
                    throw new InvalidOperationException();
                }

                // Lost the race condition and the operation has now already completed.
                // We need to invoke the continuation, but it must be asynchronously to
                // avoid a stack dive.  However, since all of the queueing mechanisms flow
                // ExecutionContext, and since we're still in the same context where we
                // captured it, we can just ignore the one we captured.
                executionContext = null;
                this.state = null; // we have the state in "state"; no need for the one in UserToken
                InvokeContinuation(continuation, state, forceAsync: true);
            }

            cb.Add(() => continuation(state));
        }

        private void InvokeContinuation(Action<object> continuation, object state, bool forceAsync)
        {
            if (continuation == null)
                return;

            object scheduler = this.scheduler;
            this.scheduler = null;
            if (scheduler != null)
            {
                if (scheduler is SynchronizationContext sc)
                {
                    sc.Post(s =>
                    {
                        var t = (Tuple<Action<object>, object>)s;
                        t.Item1(t.Item2);
                    }, Tuple.Create(continuation, state));
                }
                else
                {
                    Debug.Assert(scheduler is TaskScheduler, $"Expected TaskScheduler, got {scheduler}");
                    Task.Factory.StartNew(continuation, state, CancellationToken.None, TaskCreationOptions.DenyChildAttach, (TaskScheduler)scheduler);
                }
            }
            else if (forceAsync)
            {
                ThreadPool.QueueUserWorkItem(continuation, state, preferLocal: true);
            }
            else
            {
                continuation(state);
            }
        }
    }

    public ValueTask WriteToBufferAsync(ReadOnlyMemory<byte> bytes)
    {
        lock (_sync)
        {
            if (Length + bytes.Length > _maxiumBufferSize && _maxiumBufferSize >= 0)
            {
                var source = new _source();
                Action ac = null;
                ac = () =>
                {
                    _memoryUnderLimit -= ac;
                    WriteToBufferNoCheck(bytes.Span);
                    source.Success();
                };
                _memoryUnderLimit += ac;
                return new ValueTask(source, 0);
            }
        }

        WriteToBufferNoCheck(bytes.Span);
        return default;
    }

    public void WriteToBuffer(ReadOnlySpan<byte> bytes)
    {
        while (Length + bytes.Length > _maxiumBufferSize && _maxiumBufferSize >= 0)
        {
            Thread.Yield();
        }
        WriteToBufferNoCheck(bytes);
    }

    private void TakeOutMemoryNoCheck(Span<byte> buffer)
    {
        lock (_sync)
        {
            var discardBuffers = new List<byte[]>();
            bool prevDiscarded = false;
            if (Length < buffer.Length && _maxiumBufferSize >= 0)
            {
                throw new InvalidProgramException();
            }
            foreach (var b in _buffers)
            {
                if (buffer.Length == 0)
                {
                    break;
                }
                var start = 0;
                var end = BufferSegmentSize;
                var isFirst = b == _buffers.First() || prevDiscarded;
                var isLast = b == _buffers.Last();
                if (isFirst)
                {
                    start = _bufferStart;
                }
                if (isLast)
                {
                    end = _bufferEnd;
                }
                var length = end - start;
                var needToCopy = Math.Min(buffer.Length, length);

                b.AsSpan(start, needToCopy).CopyTo(buffer);

                start += needToCopy;
                if (isFirst)
                {
                    _bufferStart += needToCopy;
                }

                if (end - start == 0)
                {
                    if (isFirst)
                    {
                        _bufferStart = 0;
                    }
                    if (isLast)
                    {
                        _bufferEnd = 0;
                    }
                    discardBuffers.Add(b);
                    prevDiscarded = true;
                }
                else
                {
                    prevDiscarded = false;
                }

                buffer = buffer.Slice(needToCopy);
            }
            //Console.WriteLine(Length);
            Debug.Assert(buffer.Length == 0 || _maxiumBufferSize < 0);
            while (discardBuffers.Any())
            {
                var b = discardBuffers.First();
                _arrayPool.Return(b);
                discardBuffers.Remove(b);
                _buffers.Remove(b);
            }
            if (!_buffers.Any())
            {
                AddNewBufferSegment();
            }
        }
        if (Length <= _maxiumBufferSize && _maxiumBufferSize >= 0)
        {
            _memoryUnderLimit?.Invoke();
        }
    }

    public ValueTask TakeOutMemoryAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        lock (_sync)
        {
            if (buffer.Length > Length && _maxiumBufferSize >= 0)
            {
                var source = new _source();
                var reg = ct.Register(() =>
                {
                    source.Cancel();
                });
                Action ac = null;
                ac = () =>
                {
                    if (buffer.Length <= Length)
                    {
                        _dataWritten -= ac;
                        reg.Dispose();
                        TakeOutMemoryNoCheck(buffer.Span);
                        source.Success();
                    }
                };
                _dataWritten += ac;
                return new ValueTask(source, 0);
            }
        }

        TakeOutMemoryNoCheck(buffer.Span);
        return default;
    }

    public void TakeOutMemory(Span<byte> buffer)
    {
        while (buffer.Length > Length && _maxiumBufferSize >= 0)
        {
            Thread.Yield();
        }
        TakeOutMemoryNoCheck(buffer);
    }

    #region IDisposable Support
    private bool disposedValue = false;

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                foreach (var buffer in _buffers)
                {
                    _arrayPool.Return(buffer);
                }
                _buffers.Clear();
            }
            disposedValue = true;
        }
    }

    // ~UnlimitedBuffer() {
    //   Dispose(false);
    // }

    public void Dispose()
    {
        Dispose(true);
        // GC.SuppressFinalize(this);
    }
    #endregion
}