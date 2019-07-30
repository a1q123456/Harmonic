using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Harmonic.NetWorking.Rtmp.Data;
using Harmonic.NetWorking.Rtmp.Messages;

namespace Harmonic.NetWorking.Rtmp
{
    public class RtmpControlMessageStream : RtmpMessageStream
    {
        private static readonly uint CONTROL_MSID = 0;

        internal RtmpControlMessageStream(RtmpSession rtmpSession) : base(rtmpSession, CONTROL_MSID)
        {
        }
    }
}
