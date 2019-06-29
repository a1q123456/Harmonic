using Harmonic.NetWorking.Rtmp.Common;
using Harmonic.NetWorking.Rtmp.Data;
using Harmonic.NetWorking.Rtmp.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

namespace Harmonic.NetWorking.Rtmp.Serialization.Amf0
{
    public partial class Amf0BitConverter
    {
        public IReadOnlyDictionary<string, Type> RegisteredTypes => _registeredTypes;
        private Dictionary<string, Type> _registeredTypes = new Dictionary<string, Type>();
        private static readonly int TIMEZONE_LENGTH = 2;
        private static readonly int MARKER_LENGTH = 1;
        private static readonly int STRING_HEADER_LENGTH = sizeof(ushort);
        private static readonly int LONG_STRING_HEADER_LENGTH = sizeof(uint);
        private List<object> _readReferenceTable = new List<object>();
        private List<object> _writeReferenceTable = new List<object>();

        public void RegisterType<T>()
        {
            _registeredTypes.Add(typeof(T).Name, typeof(T));
        }

        public Amf0BitConverter()
        {
            var readDataHandlers = new Dictionary<Amf0Type, ReadDataHandler>();
            readDataHandlers[Amf0Type.Number] = OutValueTypeEraser<double>(TryGetNumber);
            readDataHandlers[Amf0Type.Boolean] = OutValueTypeEraser<bool>(TryGetBoolean);
            readDataHandlers[Amf0Type.String] = OutValueTypeEraser<string>(TryGetString);
            readDataHandlers[Amf0Type.Object] = OutValueTypeEraser<Dictionary<string, object>>(TryGetObject);
            readDataHandlers[Amf0Type.Null] = OutValueTypeEraser<object>(TryGetNull);
            readDataHandlers[Amf0Type.Undefined] = OutValueTypeEraser<Undefined>(TryGetUndefined);
            readDataHandlers[Amf0Type.Reference] = OutValueTypeEraser<ushort>(TryGetReference);
            readDataHandlers[Amf0Type.EcmaArray] = OutValueTypeEraser<Dictionary<string, object>>(TryGetEcmaArray);
            readDataHandlers[Amf0Type.StrictArray] = OutValueTypeEraser<List<object>>(TryGetStrictArray);
            readDataHandlers[Amf0Type.Date] = OutValueTypeEraser<DateTime>(TryGetDate);
            readDataHandlers[Amf0Type.LongString] = OutValueTypeEraser<string>(TryGetLongString);
            readDataHandlers[Amf0Type.Unsupported] = OutValueTypeEraser<Unsupported>(TryGetUnsupported);
            readDataHandlers[Amf0Type.XmlDocument] = OutValueTypeEraser<XmlDocument>(TryGetXmlDocument);
            readDataHandlers[Amf0Type.TypedObject] = OutValueTypeEraser<object>(TryGetTypedObject);
            _readDataHandlers = readDataHandlers;

            var getBytesHandlers = new Dictionary<Type, GetBytesHandler>();
            getBytesHandlers[typeof(double)] = GetBytesWrapper<double>(TryGetBytes);
            getBytesHandlers[typeof(int)] = GetBytesWrapper<double>(TryGetBytes);
            getBytesHandlers[typeof(short)] = GetBytesWrapper<double>(TryGetBytes);
            getBytesHandlers[typeof(long)] = GetBytesWrapper<double>(TryGetBytes);
            getBytesHandlers[typeof(uint)] = GetBytesWrapper<double>(TryGetBytes);
            getBytesHandlers[typeof(ushort)] = GetBytesWrapper<double>(TryGetBytes);
            getBytesHandlers[typeof(ulong)] = GetBytesWrapper<double>(TryGetBytes);
            getBytesHandlers[typeof(float)] = GetBytesWrapper<double>(TryGetBytes);
            getBytesHandlers[typeof(DateTime)] = GetBytesWrapper<DateTime>(TryGetBytes);
            getBytesHandlers[typeof(string)] = GetBytesWrapper<string>(TryGetBytes);
            getBytesHandlers[typeof(XmlDocument)] = GetBytesWrapper<XmlDocument>(TryGetBytes);
            getBytesHandlers[typeof(Unsupported)] = GetBytesWrapper<Unsupported>(TryGetBytes);
            getBytesHandlers[typeof(Undefined)] = GetBytesWrapper<Undefined>(TryGetBytes);
            getBytesHandlers[typeof(bool)] = GetBytesWrapper<bool>(TryGetBytes);
            getBytesHandlers[typeof(object)] = GetBytesWrapper<object>(TryGetBytes);
            _getBytesHandlers = getBytesHandlers;
        }

        public void ClearReaderReference()
        {
            _readReferenceTable.Clear();
        }

        public void ClearWriterReference()
        {
            _writeReferenceTable.Clear();
        }
    }
}
