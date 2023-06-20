using System;

namespace Harmonic.Networking.Rtmp.Data;

public enum UserControlMessageEvents : UInt16
{
    StreamBegin,
    StreamEof,
    StreamDry,
    SetBufferLength,
    StreamIsRecorded,
    PingRequest,
    PingResponse
}