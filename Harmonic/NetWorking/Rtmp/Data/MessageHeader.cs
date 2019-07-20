using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Networking.Rtmp.Data
{
    public class MessageHeader
    {
        public uint Timestamp { get; set; }
        public uint MessageLength { get; internal set; }
        public MessageType MessageType { get; internal set; } = 0;
        public uint? MessageStreamId { get; internal set; } = null;
    }
}
