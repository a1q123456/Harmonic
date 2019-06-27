using System;
using System.Collections.Generic;
using System.Text;
using Harmonic.NetWorking.Rtmp.Data;

namespace Harmonic.NetWorking.Rtmp.Messages
{
    public class AcknowledgementMessage : ControlMessage
    {
        public uint BytesReceived { get; set; }

        public AcknowledgementMessage() : base(MessageType.Acknowledgement)
        {
        }
    }
}
