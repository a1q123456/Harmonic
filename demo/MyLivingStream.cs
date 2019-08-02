using Harmonic.Controllers;
using Harmonic.Controllers.Living;
using Harmonic.Rpc;
using Harmonic.Service;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace demo
{
    [NeverRegister]
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
            // Do some check or other stuff

            await base.Play(streamName, start, duration, reset);
        }
    }
}
