using System;
using System.Collections.Generic;
using System.Text;
using Harmonic.Networking.Rtmp.Data;

namespace Harmonic.Networking.Rtmp.Messages
{
    public class SetChunkSizeMessage : ControlMessage
    {
        public uint ChunkSize { get; set; }

        public SetChunkSizeMessage() : base(MessageType.SetChunkSize)
        {
            
        }
    }
}
