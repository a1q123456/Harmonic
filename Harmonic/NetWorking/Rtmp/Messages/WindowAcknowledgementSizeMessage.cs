using System;
using System.Collections.Generic;
using System.Text;
using Harmonic.Networking.Rtmp.Data;

namespace Harmonic.Networking.Rtmp.Messages
{
    public class WindowAcknowledgementSizeMessage : ControlMessage
    {
        public uint WindowSize { get; set; }

        public WindowAcknowledgementSizeMessage() : base(MessageType.WindowAcknowledgementSize)
        {
        }
    }
}
