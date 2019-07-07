using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Serialization;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Harmonic.Networking.Rtmp.Messages
{
    [RtmpMessage(MessageType.Amf0Data, MessageType.Amf3Data)]
    public class DataMessage : Message
    {
        public object Data { get; set; }

        public override void Deserialize(SerializationContext context)
        {
            if (MessageHeader.MessageType == MessageType.Amf0Data)
            {
                if (!context.Amf0Reader.TryGetValue(context.ReadBuffer, out _, out var data, out _))
                {
                    throw new ProtocolViolationException();
                }
                Data = data;
            }
            else
            {
                if (!context.Amf3Reader.TryGetValue(context.ReadBuffer, out var data, out _))
                {
                    throw new ProtocolViolationException();
                }
                Data = data;
            }

        }

        public override void Serialize(SerializationContext context)
        {
            if (MessageHeader.MessageType == MessageType.Amf0Data)
            {
                var sc = new Amf.Serialization.Amf0.SerializationContext(context.WriteBuffer);
                context.Amf0Writer.WriteValueBytes(Data, sc);
            }
            else
            {
                var sc = new Amf.Serialization.Amf3.SerializationContext(context.WriteBuffer);
                context.Amf3Writer.WriteValueBytes(Data, sc);
            }
        }
    }
}
