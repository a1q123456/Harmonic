using System;
using System.Collections.Generic;
using System.Text;
using Harmonic.Networking.Rtmp.Data;

namespace Harmonic.Networking.Rtmp.Messages
{
    public enum UserControlEventType : ushort
    {
        StreamBegin,
        StreamEOF,
        StreamDry,
        SetBufferLength,
        StreamIsRecorded,
        PingRequest,
        PingResponse
    }

    public class UserControlMessage : ControlMessage
    {
        public UserControlEventType UserControlEventType { get; set; }
        public object EventData { get; set; }

        public UserControlMessage() : base(MessageType.UserControlMessages)
        {
        }
    }
}
