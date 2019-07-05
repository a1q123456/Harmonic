using Harmonic.Buffers;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Networking.Amf.Serialization.Amf0
{
    public class SerializationContext : IDisposable
    {
        public UnlimitedBuffer Buffer { get; set; } = new UnlimitedBuffer();
        public List<object> ReferenceTable { get; set; } = new List<object>();

        public int MessageLength => Buffer.BufferLength;

        public void Dispose()
        {
            ((IDisposable)Buffer).Dispose();
        }

        public void GetMessage(Span<byte> buffer)
        {
            ReferenceTable.Clear();
            Buffer.TakeOutMemory(buffer);
        }
    }
}
