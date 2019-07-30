using Harmonic.NetWorking.Rtmp.Serialization;
using Harmonic.NetWorking.Rtmp.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Messages.Commands
{
    [RtmpCommand(Name = "receiveVideo")]
    public class ReceiveVideoCommandMessage : CommandMessage
    {
        [OptionalArgument]
        public bool IsReceive { get; set; }

        public ReceiveVideoCommandMessage(AmfEncodingVersion encoding) : base(encoding)
        {
        }
    }
}
