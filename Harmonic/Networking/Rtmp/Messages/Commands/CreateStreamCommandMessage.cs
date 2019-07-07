using System;
using System.Collections.Generic;
using System.Text;
using Harmonic.Networking.Rtmp.Serialization;

namespace Harmonic.Networking.Rtmp.Messages.Commands
{
    [RtmpCommand(Name = "createStream")]
    public class CreateStreamCommandMessage : CommandMessage
    {
        public CreateStreamCommandMessage(AmfEncodingVersion encoding) : base(encoding)
        {
        }
    }
}
