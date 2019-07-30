using Harmonic.NetWorking.Rtmp.Serialization;
using Harmonic.NetWorking.Rtmp.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Messages.Commands
{
    [RtmpCommand(Name = "pause")]
    public class PauseCommandMessage : CommandMessage
    {
        [OptionalArgument]
        public bool IsPause { get; set; }
        [OptionalArgument]
        public double MilliSeconds { get; set; }

        public PauseCommandMessage(AmfEncodingVersion encoding) : base(encoding)
        {
        }
    }
}
