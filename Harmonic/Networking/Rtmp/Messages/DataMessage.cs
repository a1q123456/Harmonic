using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Serialization;
using System;
using System.Collections.Generic;
using System.Net;

namespace Harmonic.Networking.Rtmp.Messages
{
    [RtmpMessage(MessageType.Amf0Data, MessageType.Amf3Data)]
    public class DataMessage : Message
    {
#pragma warning disable CA2227 // Data can be reassigned
        public List<object> Data { get; set; }
#pragma warning restore CA2227
        public DataMessage(AmfEncodingVersion encoding) : base()
        {
            MessageHeader.MessageType = encoding == AmfEncodingVersion.Amf0 ? MessageType.Amf0Data : MessageType.Amf3Data;
        }

        public override void Deserialize(SerializationContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(paramName: nameof(context));
            }
            Data = new List<object>();
            var span = context.ReadBuffer.Span;
            if (MessageHeader.MessageType == MessageType.Amf0Data)
            {
                while (span.Length != 0)
                {
                    if (!context.Amf0Reader.TryGetValue(span, out _, out var data, out var consumed))
                    {
                        throw new ProtocolViolationException();
                    }
                    Data.Add(data);
                    span = span.Slice(consumed);
                }

            }
            else
            {
                while (span.Length != 0)
                {
                    if (!context.Amf3Reader.TryGetValue(span, out var data, out var consumed))
                    {
                        throw new ProtocolViolationException();
                    }
                    Data.Add(data);
                    span = span.Slice(consumed);
                }
            }
        }

        public override void Serialize(SerializationContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(paramName: nameof(context));
            }
            if (MessageHeader.MessageType == MessageType.Amf0Data)
            {
#pragma warning disable CA2000 // SerializationContext will use context.WriteBuffer as a Buffer object, we don't need to dispose it
#pragma warning disable IDE0068 
                var sc = new Amf.Serialization.Amf0.SerializationContext(context.WriteBuffer);
#pragma warning restore IDE0068
#pragma warning restore CA2000
                foreach (var data in Data)
                {
                    context.Amf0Writer.WriteValueBytes(data, sc);
                }
            }
            else
            {
#pragma warning disable CA2000 // SerializationContext will use context.WriteBuffer as a Buffer object, we don't need to dispose it
#pragma warning disable IDE0068 
                var sc = new Amf.Serialization.Amf3.SerializationContext(context.WriteBuffer);
#pragma warning restore IDE0068
#pragma warning restore CA2000
                foreach (var data in Data)
                {
                    context.Amf3Writer.WriteValueBytes(data, sc);
                }
            }
        }
    }
}
