using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Harmonic.Networking.Rtmp.Data
{
    public class SharedObjectMessage
    {
        public string SharedObjectName { get; set; }
        public UInt16 CurrentVersion { get; set; }
        // TBD
    }
}
