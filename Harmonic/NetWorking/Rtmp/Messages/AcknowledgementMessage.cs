using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Serialization;
using Harmonic.Networking.Utils;

namespace Harmonic.Networking.Rtmp.Messages
{
    public class AcknowledgementMessage : ControlMessage
    {
        public uint BytesReceived { get; set; }

        public AcknowledgementMessage() : base(MessageType.Acknowledgement)
        {
        }

        public override void Deserialize(SerializationContext context)
        {
            BytesReceived = NetworkBitConverter.ToUInt32(context.ReadBuffer);
        }

        public override void Serialize(SerializationContext context)
        {
            var buffer = _arrayPool.Rent(length);
            try
            {
                NetworkBitConverter.TryGetBytes(BytesReceived, buffer);
                context.WriteBuffer.WriteToBuffer(buffer.AsSpan(0, sizeof(uint)));
            }
            finally
            {
                _arrayPool.Return(buffer);
            }
            
        }
    }
}
