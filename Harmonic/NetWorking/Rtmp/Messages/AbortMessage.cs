using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Utils;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Networking.Rtmp.Messages
{
    public class AbortMessage : ControlMessage
    {
        public uint AbortedChunkStreamId { get; set; }

        public AbortMessage() : base(MessageType.AbortMessage)
        {
        }

        public override void Deserialize(byte[] buffer)
        {
            AbortedChunkStreamId = NetworkBitConverter.ToUInt32(buffer);
        }

        public override void Serialize(ArrayPool<byte> arrayPool, out byte[] buffer, out uint length)
        {
            buffer = arrayPool.Rent(sizeof(uint));
            NetworkBitConverter.TryGetBytes(AbortedChunkStreamId, buffer);
            length = sizeof(uint);
        }
    }
}
