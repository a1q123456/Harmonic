using Harmonic.NetWorking.Rtmp.Serialization;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Data
{
    public abstract class Message
    {
        protected ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
        public MessageHeader MessageHeader { get; internal set; } = new MessageHeader();
        internal Message()
        {
        }

        public abstract void Deserialize(SerializationContext context);
        public abstract void Serialize(SerializationContext context);

    }
}
