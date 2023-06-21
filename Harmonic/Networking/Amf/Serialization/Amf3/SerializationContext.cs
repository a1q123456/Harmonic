using System;
using System.Collections.Generic;
using Harmonic.Buffers;

namespace Harmonic.Networking.Amf.Serialization.Amf3;

public class SerializationContext : IDisposable
{
    public ByteBuffer Buffer { get; }
    public List<object> ObjectReferenceTable { get; set; } = new();
    public List<string> StringReferenceTable { get; set; } = new();
    public List<Amf3ClassTraits> ObjectTraitsReferenceTable { get; set; } = new();

    public int MessageLength => Buffer.Length;
    private readonly bool _disposeBuffer = true;

    public SerializationContext()
    {
        Buffer = new ByteBuffer();
    }

    public SerializationContext(ByteBuffer buffer)
    {
        Buffer = buffer;
        _disposeBuffer = false;
    }

    public void Dispose()
    {
        if (_disposeBuffer)
        {
            ((IDisposable)Buffer).Dispose();
        }
    }

    public void GetMessage(Span<byte> buffer)
    {
        ObjectReferenceTable.Clear();
        StringReferenceTable.Clear();
        ObjectTraitsReferenceTable.Clear();
        Buffer.TakeOutMemory(buffer);
    }

}