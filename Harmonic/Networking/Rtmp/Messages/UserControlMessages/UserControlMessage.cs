using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Serialization;

namespace Harmonic.Networking.Rtmp.Messages.UserControlMessages;

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