using Harmonic.Networking.Rtmp.Serialization;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Networking.Rtmp.Messages.Commands
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
