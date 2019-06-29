using Harmonic.NetWorking.Rtmp.Common;
using Harmonic.NetWorking.Rtmp.Data;
using System;
using System.Linq;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Dynamic;
using System.IO;

namespace Harmonic.NetWorking.Rtmp.Serialization.Amf3
{
    public partial class Amf3BitConverter
    {
        private readonly int MARKER_LENGTH = 1;
        private readonly IReadOnlyList<Amf3Type> _supportedTypes = null;


        public Amf3BitConverter()
        {
            var dataLengthMap = new List<Amf3Type>()
            {
                 Amf3Type.Undefined ,
                 Amf3Type.Null ,
                 Amf3Type.False ,
                 Amf3Type.True,
                 Amf3Type.Integer ,
                 Amf3Type.String ,
                 Amf3Type.XmlDocument ,
                 Amf3Type.Date ,
                 Amf3Type.Array ,
                 Amf3Type.Object ,
                 Amf3Type.ByteArray ,
                 Amf3Type.VectorObject ,
                 Amf3Type.VectorDouble ,
                 Amf3Type.VectorInt ,
                 Amf3Type.VectorUInt ,
                 Amf3Type.Dictionary
            };
            _supportedTypes = dataLengthMap;

            var readerHandlers = new Dictionary<Amf3Type, ReaderHandler>
            {
                [Amf3Type.Undefined] = ReaderHandlerWrapper<Undefined>(TryGetUndefined),
                [Amf3Type.Null] = ReaderHandlerWrapper<object>(TryGetNull),
                [Amf3Type.True] = ReaderHandlerWrapper<bool>(TryGetTrue),
                [Amf3Type.False] = ReaderHandlerWrapper<bool>(TryGetFalse),
                [Amf3Type.Integer] = ReaderHandlerWrapper<uint>(TryGetUInt29),
                [Amf3Type.String] = ReaderHandlerWrapper<string>(TryGetString),
                [Amf3Type.Xml] = ReaderHandlerWrapper<Amf3Xml>(TryGetXml),
                [Amf3Type.XmlDocument] = ReaderHandlerWrapper<XmlDocument>(TryGetXmlDocument),
                [Amf3Type.Date] = ReaderHandlerWrapper<DateTime>(TryGetDate),
                [Amf3Type.ByteArray] = ReaderHandlerWrapper<byte[]>(TryGetByteArray),
                [Amf3Type.VectorDouble] = ReaderHandlerWrapper<Vector<double>>(TryGetVectorDouble),
                [Amf3Type.VectorInt] = ReaderHandlerWrapper<Vector<int>>(TryGetVectorInt),
                [Amf3Type.VectorUInt] = ReaderHandlerWrapper<Vector<uint>>(TryGetVectorUInt),
                [Amf3Type.VectorObject] = ReaderHandlerWrapper<object>(TryGetVectorObject),
                [Amf3Type.Array] = ReaderHandlerWrapper<Dictionary<string, object>>(TryGetArray),
                [Amf3Type.Object] = ReaderHandlerWrapper<object>(TryGetObject),
                [Amf3Type.Dictionary] = ReaderHandlerWrapper<Amf3Dictionary<object, object>>(TryGetDictionary)
            };
            _readerHandlers = readerHandlers;

            var writeHandlers = new Dictionary<Type, WriteHandler>();
            writeHandlers[typeof(int)] = WriteHandlerWrapper<uint>(TryGetU29Bytes);
            writeHandlers[typeof(uint)] = WriteHandlerWrapper<uint>(TryGetU29Bytes);
            writeHandlers[typeof(long)] = WriteHandlerWrapper<uint>(TryGetU29Bytes);
            writeHandlers[typeof(ulong)] = WriteHandlerWrapper<uint>(TryGetU29Bytes);
            writeHandlers[typeof(short)] = WriteHandlerWrapper<uint>(TryGetU29Bytes);
            writeHandlers[typeof(ushort)] = WriteHandlerWrapper<uint>(TryGetU29Bytes);
            writeHandlers[typeof(double)] = WriteHandlerWrapper<double>(TryGetBytes);
            writeHandlers[typeof(Undefined)] = WriteHandlerWrapper<Undefined>(TryGetBytes);
            writeHandlers[typeof(object)] = WriteHandlerWrapper<object>(TryGetBytes);
            writeHandlers[typeof(DateTime)] = WriteHandlerWrapper<DateTime>(TryGetBytes);
            writeHandlers[typeof(XmlDocument)] = WriteHandlerWrapper<XmlDocument>(TryGetBytes);
            writeHandlers[typeof(Amf3Xml)] = WriteHandlerWrapper<Amf3Xml>(TryGetBytes);
            writeHandlers[typeof(bool)] = WriteHandlerWrapper<bool>(TryGetBytes);
            writeHandlers[typeof(byte[])] = WriteHandlerWrapper<byte[]>(TryGetBytes);
            writeHandlers[typeof(string)] = WriteHandlerWrapper<string>(TryGetBytes);
            writeHandlers[typeof(Vector<int>)] = WriteHandlerWrapper<Vector<int>>(TryGetBytes);
            writeHandlers[typeof(Vector<uint>)] = WriteHandlerWrapper<Vector<uint>>(TryGetBytes);
            writeHandlers[typeof(Vector<double>)] = WriteHandlerWrapper<Vector<double>>(TryGetBytes);
            _writeHandlers = writeHandlers;
        }

        public void RegisterTypedObject<T>() where T : new()
        {
            var type = typeof(T);
            _registeredTypedObeject.Add(type.Name, type);
        }

        public void RegisterExternalizable<T>() where T : IExternalizable, new()
        {
            var type = typeof(T);
            _registeredExternalizable.Add(type.Name, type);
        }

        public void ClearReferenceForReader()
        {
            _readObjectReferenceTable.Clear();
            _readObjectTraitsReferenceTable.Clear();
            _readStringReferenceTable.Clear();
        }

        public void ClearReferenceForWriter()
        {
            _writeObjectReferenceTable.Clear();
            _writeObjectTraitsReferenceTable.Clear();
            _writeStringReferenceTable.Clear();
        }

        private string XmlToString(XmlDocument xml)
        {
            using (var stringWriter = new StringWriter())
            using (var xmlTextWriter = XmlWriter.Create(stringWriter))
            {
                xml.WriteTo(xmlTextWriter);
                xmlTextWriter.Flush();
                return stringWriter.GetStringBuilder().ToString();
            }
        }
    }
}
