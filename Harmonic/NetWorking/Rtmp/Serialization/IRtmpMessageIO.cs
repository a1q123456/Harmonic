using Harmonic.Networking.Rtmp.Data;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Networking.Rtmp.Serialization
{
    interface IRtmpMessageIO
    {
        Message ParseMessage(MessageHeader header, byte[] body);
        void GetBytes(ArrayPool<byte> arrayPool, Message message, out byte[] buffer, out uint length);
    }
}
