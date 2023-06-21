using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Serialization;
using System;

namespace Harmonic.Networking.Rtmp.Messages;

[RtmpMessage(MessageType.AudioMessage)]
public sealed class AudioMessage : Message, ICloneable
{
    public ReadOnlyMemory<byte> Data { get; private set; }
    public object Clone()
    {
        var ret = new AudioMessage
        {
            MessageHeader = (MessageHeader)MessageHeader.Clone()
        };
        ret.MessageHeader.MessageStreamId = null;
        ret.Data = Data;
        return ret;
    }

    public override void Deserialize(SerializationContext context)
    {
        // TODO: optimize performance
        var data = new byte[context.ReadBuffer.Length];
        context.ReadBuffer.Span[..(int)this.MessageHeader.MessageLength].CopyTo(data);
        Data = data;
    }

    public override void Serialize(SerializationContext context)
    {
        context.WriteBuffer.WriteToBuffer(Data.Span[..Data.Length]);
    }
}