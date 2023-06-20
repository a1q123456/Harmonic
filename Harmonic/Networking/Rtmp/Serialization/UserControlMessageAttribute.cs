using Harmonic.Networking.Rtmp.Messages.UserControlMessages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Networking.Rtmp.Serialization;

public class UserControlMessageAttribute : Attribute
{
    public UserControlEventType Type { get; set; }
}