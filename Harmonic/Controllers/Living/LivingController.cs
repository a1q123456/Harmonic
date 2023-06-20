using Harmonic.Networking.Rtmp;
using Harmonic.Rpc;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Controllers.Living;

public class LivingController : RtmpController
{
    [RpcMethod("createStream")]
    public uint CreateStream()
    {
        var stream = RtmpSession.CreateNetStream<LivingStream>();
        return stream.MessageStream.MessageStreamId;
    }
}