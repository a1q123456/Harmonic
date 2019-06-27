using System;
using System.Collections.Generic;
using System.Text;
using Harmonic.NetWorking.Rtmp.Data;

namespace Harmonic.NetWorking.Rtmp.Messages
{
    public class WindowAcknowledgementSizeMessage : ControlMessage
    {
        public uint WindowSize { get; set; }

        public WindowAcknowledgementSizeMessage() : base(MessageType.WindowAcknowledgementSize)
        {
        }
    }
}
