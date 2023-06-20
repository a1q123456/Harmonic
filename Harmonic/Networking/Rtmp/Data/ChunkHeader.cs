using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Harmonic.Networking.Rtmp.Data;

class ChunkHeader
{
    public ChunkBasicHeader ChunkBasicHeader { get; set; }
    public MessageHeader MessageHeader { get; set; }
    public uint ExtendedTimestamp { get; set; }

}