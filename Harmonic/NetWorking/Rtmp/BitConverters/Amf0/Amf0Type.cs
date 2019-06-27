using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.BitConverters.Amf0
{
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
}
