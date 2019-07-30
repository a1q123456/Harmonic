using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace Harmonic.NetWorking.Amf.Serialization.Amf3
{
    public class Amf3Xml : XmlDocument
    {
        public Amf3Xml() : base() { }

        public Amf3Xml(XmlNameTable nt) : base(nt) { }

        protected internal Amf3Xml(XmlImplementation imp) : base(imp) { }
    }
}
