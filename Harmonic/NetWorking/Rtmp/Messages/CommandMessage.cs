using Harmonic.Networking.Rtmp.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Networking.Rtmp.Messages
{
    public enum Encoding
    {
        Amf0,
        Amf3
    }

    public abstract class CommandMessage : Message
    {
        public Encoding Encoding { get; set; }

        public CommandMessage(Encoding encoding) :
            base(encoding == Encoding.Amf0 ? MessageType.Amf0Command : MessageType.Amf3Command)
        {
            Encoding = encoding;
        }
    }
}
