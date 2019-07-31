using Harmonic.Networking.Rtmp.Serialization;
using Harmonic.Networking.Rtmp.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Networking.Rtmp.Messages.Commands
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
