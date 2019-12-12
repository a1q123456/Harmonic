using Harmonic.Networking.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Harmonic.Networking.Flv.Data
{
    public class AvcVideoPacket : AVPacket
    {
        public AvcPacketType AvcPacketType { get; }
        public uint CompositionTime { get; }
        public ReadOnlyMemory<byte> Payload { get; }
        
        public AvcVideoPacket(ReadOnlyMemory<byte> data) : base(data)
        {
            if (data.Length <= 4)
            {
                throw new InvalidDataException();
            }
            AvcPacketType = (AvcPacketType)data.Span[0];
            CompositionTime = NetworkBitConverter.ToUInt24(data.Span.Slice(1, 3));
            Payload = data.Slice(4);
        }

    }
}
