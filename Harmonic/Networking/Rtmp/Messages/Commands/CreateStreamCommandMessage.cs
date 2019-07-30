using System;
using System.Collections.Generic;
using System.Text;
using Harmonic.NetWorking.Rtmp.Serialization;
using Harmonic.NetWorking.Rtmp.Messages;

namespace Harmonic.NetWorking.Rtmp.Messages.Commands
{
    [RtmpCommand(Name = "createStream")]
    public class CreateStreamCommandMessage : CommandMessage
    {
        public CreateStreamCommandMessage(AmfEncodingVersion encoding) : base(encoding)
        {
        }
    }
}
