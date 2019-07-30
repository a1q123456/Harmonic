using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Amf.Data
{
    public class Message
    {
        public string TargetUri { get; set; }
        public string ResponseUri { get; set; }
        public object Content { get; set; }
    }
}
