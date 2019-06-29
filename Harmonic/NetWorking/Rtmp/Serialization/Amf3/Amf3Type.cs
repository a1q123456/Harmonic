using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Serialization.Amf3
{
    public enum Amf3Type : byte
    {
        Undefined,
        Null,
        False,
        True,
        Integer,
        Double,
        String,
        XmlDocument,
        Date,
        Array,
        Object,
        Xml,
        ByteArray,
        VectorInt,
        VectorUInt,
        VectorDouble,
        VectorObject,
        Dictionary
    }
}
