using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Messages;

namespace Harmonic.Networking.Rtmp
{
    public class RtmpControlMessageStream : RtmpMessageStream
    {
        private static readonly uint CONTROL_MSID = 0;

        internal RtmpControlMessageStream(RtmpSession rtmpSession) : base(rtmpSession, CONTROL_MSID)
        {
        }
    }
}
