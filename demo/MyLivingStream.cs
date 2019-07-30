using Harmonic.Controllers.Living;
using Harmonic.Rpc;
using Harmonic.Service;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace demo
{
    public class MyLivingStream : LivingStream
    {
        public MyLivingStream(PublisherSessionService publisherSessionService) : base(publisherSessionService)
        {
        }

        [RpcMethod("play")]
        public new async Task Play(
            [FromOptionalArgument] string streamName,
            [FromOptionalArgument] double start = -1,
            [FromOptionalArgument] double duration = -1,
            [FromOptionalArgument] bool reset = false)
        {
            if (streamName == "a2")
            {
                await base.Play(streamName, start, duration, reset);
            }
            else
            {
                throw new NotSupportedException();
            }
        }
    }
}
