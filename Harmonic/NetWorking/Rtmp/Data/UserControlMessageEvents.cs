using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Data
{
    public enum UserControlMessageEvents : UInt16
    {
        StreamBegin,
        StreamEOF,
        StreamDry,
        SetBufferLength,
        StreamIsRecorded,
        PingRequest,
        PingResponse
    }
}
