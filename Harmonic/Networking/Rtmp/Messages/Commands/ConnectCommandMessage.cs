using System;
using System.Collections.Generic;
using System.Text;
using Harmonic.Networking.Rtmp.Serialization;
using Harmonic.Networking.Rtmp.Messages;

namespace Harmonic.Networking.Rtmp.Messages.Commands
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
