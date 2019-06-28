using Harmonic.NetWorking.Rtmp.Common;
using Harmonic.NetWorking.Rtmp.Data;
using System;
using System.Linq;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Dynamic;

namespace Harmonic.NetWorking.Rtmp.BitConverters.Amf3
{
    public partial class Amf3BitConverter
    {
        private readonly int MARKER_LENGTH = 1;
        private readonly IReadOnlyList<Amf3Type> _supportedTypes = null;
        private List<object> _objectReferenceTable = new List<object>();
        private List<string> _stringReferenceTable = new List<string>();
        private List<Amf3ClassTraits> _objectTraitsReferenceTable = new List<Amf3ClassTraits>();


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
                 Amf3Type.VectorUint ,
                 Amf3Type.Dictionary
            };
            _supportedTypes = dataLengthMap;

            var readerHandlers = new Dictionary<Amf3Type, ReaderHandler>();
            readerHandlers[Amf3Type.Undefined] = ReaderHandlerWrapper<Undefined>(TryGetUndefined);
            readerHandlers[Amf3Type.Null] = ReaderHandlerWrapper<object>(TryGetNull);
            readerHandlers[Amf3Type.True] = ReaderHandlerWrapper<bool>(TryGetTrue);
            readerHandlers[Amf3Type.False] = ReaderHandlerWrapper<bool>(TryGetFalse);
            readerHandlers[Amf3Type.Integer] = ReaderHandlerWrapper<uint>(TryGetUInt29);
            readerHandlers[Amf3Type.String] = ReaderHandlerWrapper<string>(TryGetString);
            readerHandlers[Amf3Type.XmlDocument] = ReaderHandlerWrapper<XmlDocument>(TryGetXmlDocument);
            readerHandlers[Amf3Type.Date] = ReaderHandlerWrapper<DateTime>(TryGetDate);
            readerHandlers[Amf3Type.ByteArray] = ReaderHandlerWrapper<byte[]>(TryGetByteArray);
            readerHandlers[Amf3Type.VectorDouble] = ReaderHandlerWrapper<Vector<double>>(TryGetVector);
            readerHandlers[Amf3Type.VectorInt] = ReaderHandlerWrapper<Vector<int>>(TryGetVector);
            readerHandlers[Amf3Type.VectorUint] = ReaderHandlerWrapper<Vector<uint>>(TryGetVector);
            readerHandlers[Amf3Type.VectorObject] = ReaderHandlerWrapper<Vector<object>>(TryGetVector);
            readerHandlers[Amf3Type.Array] = ReaderHandlerWrapper<Dictionary<string, object>>(TryGetArray);
            readerHandlers[Amf3Type.Object] = ReaderHandlerWrapper<object>(TryGetObject);
            readerHandlers[Amf3Type.Dictionary] = ReaderHandlerWrapper<Dictionary<object, object>>(TryGetDictionary);
            _readerHandlers = readerHandlers;
        }

        public void RegisterTypedObject<T>() where T : new()
        {
            var type = typeof(T);
            _registeredTypedObeject.Add(type.Name, type);
        }

        public void RegisterExternalizable<T>(T externalizable) where T : IExternalizable
        {
            var type = typeof(T);
            _registeredExternalizable.Add(type.Name, externalizable);
        }
    }
}
