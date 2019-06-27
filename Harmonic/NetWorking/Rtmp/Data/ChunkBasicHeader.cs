using Harmonic.NetWorking.Serialization;
using Harmonic.NetWorking.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Data
{
    public class ChunkBasicHeader 
    {
        public ChunkHeaderType RtmpChunkHeaderType { get; set; }
        public uint ChunkStreamId { get; set; }
    }
}
