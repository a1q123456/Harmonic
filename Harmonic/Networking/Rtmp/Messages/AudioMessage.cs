using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Serialization;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Harmonic.Networking.Rtmp.Messages
{
    [RtmpMessage(MessageType.AudioMessage)]
    public sealed class AudioMessage : Message
    {
        public byte[] Data { get; set; }

        public override void Deserialize(SerializationContext context)
        {
            Data = new byte[context.ReadBuffer.Length];
            context.ReadBuffer.Span.Slice(0, (int)MessageHeader.MessageLength).CopyTo(Data);
        }

        public override void Serialize(SerializationContext context)
        {
            context.WriteBuffer.WriteToBuffer(Data.AsSpan(0, Data.Length));
        }
    }
}
