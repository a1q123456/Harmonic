using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Harmonic.NetWorking.Rtmp.Data;
using Harmonic.NetWorking.Rtmp.Serialization;
using Harmonic.NetWorking.Utils;

namespace Harmonic.NetWorking.Rtmp.Messages.UserControlMessages
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

    [RtmpMessage(MessageType.UserControlMessages)]
    public abstract class UserControlMessage : ControlMessage
    {
        public UserControlEventType UserControlEventType { get; set; }

        public UserControlMessage() : base()
        {
        }
        
    }

}
