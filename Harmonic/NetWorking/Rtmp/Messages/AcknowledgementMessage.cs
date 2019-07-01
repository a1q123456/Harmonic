using System;
using System.Collections.Generic;
using System.Text;
using Harmonic.Networking.Rtmp.Data;

namespace Harmonic.Networking.Rtmp.Messages
{
    public class AcknowledgementMessage : ControlMessage
    {
        public uint BytesReceived { get; set; }

        public AcknowledgementMessage() : base(MessageType.Acknowledgement)
        {
        }
    }
}
