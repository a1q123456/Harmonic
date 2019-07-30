using System;
using System.Collections.Generic;
using System.Text;
using Harmonic.NetWorking.Rtmp.Serialization;
using Harmonic.NetWorking.Rtmp.Messages;

namespace Harmonic.NetWorking.Rtmp.Messages.Commands
{
    [RtmpCommand(Name = "connect")]
    public class ConnectCommandMessage : CommandMessage
    {
        [OptionalArgument]
        public object UserArguments { get; set; }

        public ConnectCommandMessage(AmfEncodingVersion encoding) : base(encoding)
        {
        }
    }
}
