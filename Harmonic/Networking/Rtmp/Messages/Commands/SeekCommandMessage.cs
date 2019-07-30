using Harmonic.NetWorking.Rtmp.Serialization;
using Harmonic.NetWorking.Rtmp.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Messages.Commands
{
    [RtmpCommand(Name = "seek")]
    public class SeekCommandMessage : CommandMessage
    {
        [OptionalArgument]
        public double MilliSeconds { get; set; }

        public SeekCommandMessage(AmfEncodingVersion encoding) : base(encoding)
        {
        }
    }
}
