using Harmonic.Networking.Rtmp.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Networking.Rtmp.Messages
{
    public abstract class CommandMessage : Message
    {



        internal CommandMessage(MessageType messageType) : base(messageType)
        {
        }
    }
}
