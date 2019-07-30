using Harmonic.NetWorking.Rtmp.Data;
using Harmonic.NetWorking.Rtmp.Serialization;
using Harmonic.NetWorking.Rtmp.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Messages.Commands
{
    [RtmpCommand(Name = "onStatus")]
    public class OnStatusCommandMessage : CommandMessage
    {
        [OptionalArgument]
        public object InfoObject { get; set; }

        public OnStatusCommandMessage(AmfEncodingVersion encoding) : base(encoding)
        {
        }
    }
}
