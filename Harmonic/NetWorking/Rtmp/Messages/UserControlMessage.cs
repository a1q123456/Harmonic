using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Utils;

namespace Harmonic.Networking.Rtmp.Messages
{
    public enum UserControlEventType : ushort
    {
        StreamBegin,
        StreamEof,
        StreamDry,
        SetBufferLength,
        StreamIsRecorded,
        PingRequest,
        PingResponse
    }

    public abstract class UserControlMessage : ControlMessage
    {
        protected UserControlEventType UserControlEventType { get; set; }

        public UserControlMessage(UserControlEventType userControlEventType) : base(MessageType.UserControlMessages)
        {
            UserControlEventType = userControlEventType;
        }
        
    }

}
