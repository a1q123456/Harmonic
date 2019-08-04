using Harmonic.Networking.Rtmp.Messages;
using Harmonic.Rpc;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Controllers.Record
{
    public class RecordController : RtmpController
    {
        [RpcMethod("createStream")]
        public uint CreateStream()
        {
            var stream = RtmpSession.CreateNetStream<RecordStream>();
            return stream.MessageStream.MessageStreamId;
        }
    }
}
