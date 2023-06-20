using Harmonic.Networking.Rtmp.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Networking.Rtmp.Exceptions;

public class UnknownMessageReceivedException : Exception
{
    public MessageHeader Header { get; set; }

    public UnknownMessageReceivedException(MessageHeader header)
    {
        Header = header;
    }
}