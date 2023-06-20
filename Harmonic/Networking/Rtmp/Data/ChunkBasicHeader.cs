using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;

namespace Harmonic.Networking.Rtmp.Data;

class ChunkBasicHeader 
{
    public ChunkHeaderType RtmpChunkHeaderType { get; set; }
    public uint ChunkStreamId { get; set; }
}