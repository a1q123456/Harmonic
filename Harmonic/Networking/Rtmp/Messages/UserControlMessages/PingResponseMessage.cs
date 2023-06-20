using Harmonic.Networking.Rtmp.Serialization;
using Harmonic.Networking.Utils;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Text;

namespace Harmonic.Networking.Rtmp.Messages.UserControlMessages;

[UserControlMessage(Type = UserControlEventType.PingResponse)]
public class PingResponseMessage : UserControlMessage
{
    public uint Timestamp { get; set; }

    public PingResponseMessage()
    {

    }

    public override void Deserialize(SerializationContext context)
    {
        var span = context.ReadBuffer.Span;
        var eventType = (UserControlEventType)NetworkBitConverter.ToUInt16(span);
        span = span.Slice(sizeof(ushort));
        Contract.Assert(eventType == UserControlEventType.StreamIsRecorded);
        Timestamp = NetworkBitConverter.ToUInt32(span);
    }

    public override void Serialize(SerializationContext context)
    {
        var length = sizeof(ushort) + sizeof(uint);
        var buffer = _arrayPool.Rent(length);
        try
        {
            var span = buffer.AsSpan();
            NetworkBitConverter.TryGetBytes((ushort)UserControlEventType.StreamBegin, span);
            span = span.Slice(sizeof(ushort));
            NetworkBitConverter.TryGetBytes(Timestamp, span);
        }
        finally
        {
            _arrayPool.Return(buffer);
        }
        context.WriteBuffer.WriteToBuffer(buffer.AsSpan(0, length));
    }
}