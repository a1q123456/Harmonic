using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Networking.Flv.Data
{
    public class AVPacket
    {
        public ReadOnlyMemory<byte> Data { get; set; }

        public AVPacket(ReadOnlyMemory<byte> data)
        {
            Data = data;
        }
    }
}
