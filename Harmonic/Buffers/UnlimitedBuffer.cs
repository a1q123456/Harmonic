using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Harmonic.Buffers
{
    public class UnlimitedBuffer
    {
        private List<byte[]> _buffers = new List<byte[]>();
        private int _bufferIndex = 0;
        private ArrayPool<byte> _arrayPool;
        public int BufferSegmentSize { get; }
        public int BufferLength => _buffers.Sum(b => b.Length) - BufferBytesAvailable();

        public UnlimitedBuffer(int bufferSegmentSize = 1024, ArrayPool<byte> arrayPool = null)
        {
            if (bufferSegmentSize == 0)
            {
                throw new ArgumentOutOfRangeException();
            }

            BufferSegmentSize = bufferSegmentSize;
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
            if (_buffers.Any())
            {
                return _buffers.Last().Length - _bufferIndex;
            }
            return 0;
        }

        private void AddNewBufferSegment()
        {
            _buffers.Add(_arrayPool.Rent(BufferSegmentSize));
            _bufferIndex = 0;
        }

        public void WriteToBuffer(byte data)
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
            buffer[_bufferIndex] = data;
            _bufferIndex += 1;
        }

        public void WriteToBuffer(ReadOnlySpan<byte> bytes)
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
                    seq.CopyTo(buffer.AsSpan(_bufferIndex));
                    _bufferIndex += seq.Length;
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
                bytes.CopyTo(buffer.AsSpan(_bufferIndex));
                _bufferIndex += bytes.Length;
            }
        }

        public void TakeOutMemory(Span<byte> buffer)
        {
            if (buffer.Length < BufferLength)
            {
                throw new ArgumentOutOfRangeException();
            }

            foreach (var b in _buffers)
            {
                if (b == _buffers.Last())
                {
                    b.AsSpan(0, _bufferIndex).CopyTo(buffer);
                }
                else
                {
                    b.CopyTo(buffer);
                    buffer = buffer.Slice(b.Length);
                }
                _arrayPool.Return(b);
            }
            _buffers.Clear();
            AddNewBufferSegment();
        }

    }
}
