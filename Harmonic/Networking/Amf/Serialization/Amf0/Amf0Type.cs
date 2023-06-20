using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Networking.Amf.Serialization.Amf0;

public enum Amf0Type
{
    Number,
    Boolean,
    String,
    Object,
    Moveclip,
    Null,
    Undefined,
    Reference,
    EcmaArray,
    ObjectEnd,
    StrictArray,
    Date,
    LongString,
    Unsupported,
    Recordset,
    XmlDocument,
    TypedObject,
    AvmPlusObject
}