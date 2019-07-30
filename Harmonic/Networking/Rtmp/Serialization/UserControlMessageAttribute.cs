using Harmonic.NetWorking.Rtmp.Messages.UserControlMessages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Serialization
{
    public class UserControlMessageAttribute : Attribute
    {
        public UserControlEventType Type { get; set; }
    }
}
