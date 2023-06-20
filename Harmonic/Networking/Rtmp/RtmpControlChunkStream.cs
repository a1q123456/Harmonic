using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Messages;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Harmonic.Networking.Rtmp;

class RtmpControlChunkStream : RtmpChunkStream
{
    private static readonly uint CONTROL_CSID = 2;

    internal RtmpControlChunkStream(RtmpSession rtmpSession) : base()
    {
        ChunkStreamId = CONTROL_CSID;
        RtmpSession = rtmpSession;
    }
}