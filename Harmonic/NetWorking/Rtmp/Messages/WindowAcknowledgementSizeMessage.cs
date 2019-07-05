using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Serialization;
using Harmonic.Networking.Utils;

namespace Harmonic.Networking.Rtmp.Messages
{
    public class WindowAcknowledgementSizeMessage : ControlMessage
    {
        public uint WindowSize { get; set; }

        public WindowAcknowledgementSizeMessage() : base(MessageType.WindowAcknowledgementSize)
        {
        }

        public override void Deserialize(SerializationContext context)
        {
            WindowSize = NetworkBitConverter.ToUInt32(context.ReadBuffer);
        }

        public override void Serialize(SerializationContext context)
        {
            var arr = ArrayPool<byte>.Shared.Rent(sizeof(uint));
            try
            {
                NetworkBitConverter.TryGetBytes(WindowSize, arr);
                context.WriteBuffer.WriteToBuffer(arr.AsSpan(0, sizeof(uint)));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(arr);
            }
        }
    }
}
