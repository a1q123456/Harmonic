using System;
using System.Diagnostics.Contracts;
using Harmonic.Networking.Rtmp.Serialization;
using Harmonic.Networking.Utils;

namespace Harmonic.Networking.Rtmp.Messages.UserControlMessages;

[UserControlMessage(Type = UserControlEventType.StreamDry)]
public class StreamDryMessage : UserControlMessage
{
    public uint StreamID { get; set; }

    public override void Deserialize(SerializationContext context)
    {
        var span = context.ReadBuffer.Span;
        var eventType = (UserControlEventType)NetworkBitConverter.ToUInt16(span);
        span = span[sizeof(ushort)..];
        Contract.Assert(eventType == UserControlEventType.StreamIsRecorded);
        StreamID = NetworkBitConverter.ToUInt32(span);
    }

    public override void Serialize(SerializationContext context)
    {
        var length = sizeof(ushort) + sizeof(uint);
        var buffer = _arrayPool.Rent(length);
        try
        {
            var span = buffer.AsSpan();
            NetworkBitConverter.TryGetBytes((ushort)UserControlEventType.StreamBegin, span);
            span = span[sizeof(ushort)..];
            NetworkBitConverter.TryGetBytes(StreamID, span);
        }
        finally
        {
            _arrayPool.Return(buffer);
        }
        context.WriteBuffer.WriteToBuffer(buffer.AsSpan(0, length));
    }
}