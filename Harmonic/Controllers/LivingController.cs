using Harmonic.Networking.Rtmp;
using Harmonic.Rpc;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Controllers
{
    public class LivingController : AbstractController
    {


        [RpcMethod("createStream")]
        public uint CreateStream()
        {
            var stream = RtmpSession.CreateNetStream<NetStream>();
            return stream.MessageStream.MessageStreamId;
        }
    }
}
