using System.Xml;

namespace Harmonic.Networking.Amf.Serialization.Amf3;

public class Amf3Xml : XmlDocument
{
    public Amf3Xml() : base() { }

    public Amf3Xml(XmlNameTable nt) : base(nt) { }

    protected internal Amf3Xml(XmlImplementation imp) : base(imp) { }
}