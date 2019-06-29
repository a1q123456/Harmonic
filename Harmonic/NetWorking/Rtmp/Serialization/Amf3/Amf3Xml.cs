using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace Harmonic.NetWorking.Rtmp.Serialization.Amf3
{
    public class Amf3Xml : XmlDocument
    {
        public Amf3Xml() : base() { }

        public Amf3Xml(XmlNameTable nt) : base(nt) { }

        protected internal Amf3Xml(XmlImplementation imp) : base(imp) { }
    }
}
