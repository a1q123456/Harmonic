using Harmonic.NetWorking.Rtmp.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Messages
{
    public class AbortMessage : ControlMessage
    {
        public uint AbortedChunkStreamId { get; set; }

        public AbortMessage() : base(MessageType.AbortMessage)
        {
        }
    }
}
