using System;
using Harmonic.Networking.Rtmp.Messages.UserControlMessages;

namespace Harmonic.Networking.Rtmp.Serialization;

public class UserControlMessageAttribute : Attribute
{
    public UserControlEventType Type { get; set; }
}