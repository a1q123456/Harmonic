using Harmonic.NetWorking.Rtmp.Serialization;
using Harmonic.NetWorking.Rtmp.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Messages.Commands
{
    [RtmpCommand(Name = "receiveAudio")]
    public class ReceiveAudioCommandMessage : CommandMessage
    {
        [OptionalArgument]
        public bool IsReceive { get; set; }

        public ReceiveAudioCommandMessage(AmfEncodingVersion encoding) : base(encoding)
        {
        }
    }
}
