using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Serialization;
using Harmonic.Networking.Utils;

namespace Harmonic.Networking.Rtmp.Messages
{
    public enum LimitType : byte
    {
        Hard,
        Soft,
        Dynamic
    }

    public class SetPeerBandwidthMessage : ControlMessage
    {
        public uint WindowSize { get; set; }
        public LimitType LimitType { get; set; }

        public SetPeerBandwidthMessage() : base(MessageType.SetPeerBandwidth)
        {
        }

        public override void Deserialize(SerializationContext context)
        {
            WindowSize = NetworkBitConverter.ToUInt32(context.ReadBuffer);
            LimitType = (LimitType)context.ReadBuffer.AsSpan(sizeof(uint))[0];
        }

        public override void Serialize(SerializationContext context)
        {
            var buffer = _arrayPool.Rent(sizeof(uint) + sizeof(byte));
            try
            {
                NetworkBitConverter.TryGetBytes(WindowSize, buffer);
                buffer.AsSpan(sizeof(uint))[0] = (byte)LimitType;
                context.WriteBuffer.WriteToBuffer(buffer.AsSpan(0, sizeof(uint) + sizeof(byte)));
            }
            finally
            {
                _arrayPool.Return(buffer);
            }
        }
    }
}
