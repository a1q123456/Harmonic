using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.BitConverters.Amf3
{
    public enum Amf3ClassType
    {
        Anonymous,
        Typed,
        Dynamic,
        Externalizable
    }

    public class Amf3ClassTraits
    {
        public Amf3ClassType ClassType { get; set; }
        public string ClassName { get; set; }
        public List<string> Members { get; set; } = new List<string>();
    }
}
