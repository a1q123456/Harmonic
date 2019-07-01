using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Networking.Rtmp
{
    public class NetStream : RtmpMessageStream
    {
        internal NetStream(RtmpSession rtmpSession) : base(rtmpSession)
        {
        }

        public void DeleteStream()
        {
            Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            RtmpSession.NetConnection.MessageStreamDestroying(this);
        }

    }
}
