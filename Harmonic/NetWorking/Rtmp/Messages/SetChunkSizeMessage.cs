using System;
using System.Collections.Generic;
using System.Text;
using Harmonic.NetWorking.Rtmp.Data;

namespace Harmonic.NetWorking.Rtmp.Messages
{
    public class SetChunkSizeMessage : ControlMessage
    {
        public uint ChunkSize { get; set; }

        public SetChunkSizeMessage() : base(MessageType.SetChunkSize)
        {
            
        }
    }
}
