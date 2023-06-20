using Harmonic.Networking.Rtmp.Messages.UserControlMessages;
using System;

namespace Harmonic.Networking.Rtmp.Serialization;

public class UserControlMessageAttribute : Attribute
{
    public UserControlEventType Type { get; set; }
}