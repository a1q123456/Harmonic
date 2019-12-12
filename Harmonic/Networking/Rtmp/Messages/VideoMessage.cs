using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Serialization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;

namespace Harmonic.Networking.Rtmp.Messages
{
    [RtmpMessage(MessageType.VideoMessage)]
    public sealed class VideoMessage : Message, ICloneable
    {
        public VideoMessage() { }

        public static VideoMessage CreateFromData(ReadOnlySpan<byte> data)
        {
            var ret = new VideoMessage();
            var retData = new byte[data.Length];
            data.CopyTo(retData);
            ret.Data = retData;
            return ret;
        }

        public ReadOnlyMemory<byte> Data { get; private set; }

        public object Clone()
        {
            var ret = new VideoMessage
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
            Debug.Assert(context.ReadBuffer.Length == MessageHeader.MessageLength);
            context.ReadBuffer.Span.Slice(0, (int)MessageHeader.MessageLength).CopyTo(data);
            Data = data;
        }

        public override void Serialize(SerializationContext context)
        {
            context.WriteBuffer.WriteToBuffer(Data.Span.Slice(0, Data.Length));
        }

    }
}
