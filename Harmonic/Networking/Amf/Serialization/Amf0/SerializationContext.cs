using Harmonic.Buffers;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Networking.Amf.Serialization.Amf0
{
    public class SerializationContext : IDisposable
    {
        public UnlimitedBuffer Buffer { get; private set; }
        public List<object> ReferenceTable { get; set; } = new List<object>();

        public int MessageLength => Buffer.BufferLength;

        private bool _disposeBuffer = true;

        public SerializationContext()
        {
            Buffer = new UnlimitedBuffer();
        }

        public SerializationContext(UnlimitedBuffer buffer)
        {
            Buffer = buffer;
            _disposeBuffer = false;
        }

        public void GetMessage(Span<byte> buffer)
        {
            ReferenceTable.Clear();
            Buffer.TakeOutMemory(buffer);
        }

        public void Dispose()
        {
            if (_disposeBuffer)
            {
                ((IDisposable)Buffer).Dispose();
            }
        }
    }
}
