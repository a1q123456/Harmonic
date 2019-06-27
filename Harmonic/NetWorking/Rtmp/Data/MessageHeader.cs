using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Data
{
    public class MessageHeader
    {
        public uint Timestamp { get; set; }
        public uint MessageLength { get; internal set; }
        public MessageType MessageType { get; internal set; }
        public uint? MessageStreamId { get; internal set; } = null;
    }
}
