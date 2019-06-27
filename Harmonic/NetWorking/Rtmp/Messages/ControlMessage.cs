using Harmonic.NetWorking.Rtmp.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Messages
{
    public abstract class ControlMessage : Message
    {
        internal ControlMessage(MessageType messageType) : base(messageType)
        { }
    }
}
