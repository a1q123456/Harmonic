using Harmonic.NetWorking.Rtmp.Serialization;
using Harmonic.NetWorking.Rtmp.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Messages.Commands
{
    [RtmpCommand(Name = "play2")]
    public class Play2CommandMessage : CommandMessage
    {
        [OptionalArgument]
        public object Parameters { get; set; }

        public Play2CommandMessage(AmfEncodingVersion encoding) : base(encoding)
        {
        }
    }
}
