using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Serialization;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Harmonic.Networking.Rtmp.Messages
{
    public class VideoMessage : Message
    {
        public byte[] Data { get; set; }

        public override void Deserialize(SerializationContext context)
        {
            Data = _arrayPool.Rent(context.ReadBuffer.Length);
            context.ReadBuffer.AsSpan(0, (int)MessageHeader.MessageLength).CopyTo(Data);
        }

        public override void Serialize(SerializationContext context)
        {
            context.WriteBuffer.WriteToBuffer(Data.AsSpan(0, (int)MessageHeader.MessageLength));
        }
    }
}
