using System;
using Harmonic.Networking.Rtmp.Data;

namespace Harmonic.Networking.Rtmp.Exceptions;

public class UnknownMessageReceivedException : Exception
{
    public MessageHeader Header { get; set; }

    public UnknownMessageReceivedException(MessageHeader header)
    {
        Header = header;
    }
}