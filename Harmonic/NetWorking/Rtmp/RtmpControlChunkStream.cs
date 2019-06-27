using Harmonic.NetWorking.Rtmp.Data;
using Harmonic.NetWorking.Rtmp.Messages;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Harmonic.NetWorking.Rtmp
{
    class RtmpControlChunkStream : RtmpChunkStream
    {
        private static readonly uint CONTROL_CSID = 2;

        internal RtmpControlChunkStream(RtmpSession rtmpSession) : base()
        {
            ChunkStreamId = CONTROL_CSID;
            RtmpSession = rtmpSession;
        }
    }
}
