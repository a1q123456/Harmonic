using Harmonic.NetWorking.Rtmp.Serialization;
using Harmonic.NetWorking.Rtmp.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Messages.Commands
{
    [RtmpCommand(Name = "deleteStream")]
    public class DeleteStreamCommandMessage : CommandMessage
    {
        [OptionalArgument]
        public double StreamID { get; set; }

        public DeleteStreamCommandMessage(AmfEncodingVersion encoding) : base(encoding)
        {
        }
    }
}
