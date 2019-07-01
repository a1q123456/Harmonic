using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Harmonic.Networking.Rtmp.Data
{
    public abstract class Message
    {
        public MessageHeader MessageHeader { get; } = new MessageHeader();
        internal Message( MessageType messageType)
        {
            MessageHeader.MessageType = messageType;
        }
    }
}
