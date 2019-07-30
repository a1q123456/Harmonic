using Harmonic.NetWorking.Rtmp.Data;
using Harmonic.NetWorking.Rtmp.Serialization;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Messages
{
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
            var data = new byte[context.ReadBuffer.Length];
            context.ReadBuffer.Span.Slice(0, (int)MessageHeader.MessageLength).CopyTo(data);
            Data = data;
        }

        public override void Serialize(SerializationContext context)
        {
            context.WriteBuffer.WriteToBuffer(Data.Span.Slice(0, Data.Length));
        }
    }
}
