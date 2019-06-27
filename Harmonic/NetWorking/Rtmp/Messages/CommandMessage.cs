using Harmonic.NetWorking.Rtmp.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Messages
{
    public abstract class CommandMessage : Message
    {



        internal CommandMessage(MessageType messageType) : base(messageType)
        {
        }
    }
}
