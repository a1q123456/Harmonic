using Harmonic.Controllers.Living;
using Harmonic.Rpc;
using System;
using System.Collections.Generic;
using System.Text;

namespace demo
{
    class MyLivingController : LivingController
    {
        [RpcMethod("createStream")]
        public new uint CreateStream()
        {
            var stream = RtmpSession.CreateNetStream<MyLivingStream>();
            return stream.MessageStream.MessageStreamId;
        }
    }
}
