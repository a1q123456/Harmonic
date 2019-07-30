using Harmonic.NetWorking.Rtmp.Serialization;
using Harmonic.NetWorking.Rtmp.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Messages.Commands
{
    [RtmpCommand(Name = "publish")]
    public class PublishCommandMessage : CommandMessage
    {
        [OptionalArgument]
        public string PublishingName { get; set; }
        [OptionalArgument]
        public string PublishingType { get; set; }

        public PublishCommandMessage(AmfEncodingVersion encoding) : base(encoding)
        {
        }
    }
}
