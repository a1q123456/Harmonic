using Harmonic.Networking.Amf.Common;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

namespace Harmonic.Networking.Amf.Serialization.Amf0
{
    public static class Amf0CommonValues
    {
        public static readonly int TIMEZONE_LENGTH = 2;
        public static readonly int MARKER_LENGTH = 1;
        public static readonly int STRING_HEADER_LENGTH = sizeof(ushort);
        public static readonly int LONG_STRING_HEADER_LENGTH = sizeof(uint);
    }
}
