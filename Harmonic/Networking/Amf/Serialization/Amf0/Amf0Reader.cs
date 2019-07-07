using Harmonic.Networking.Amf.Attributes;
using Harmonic.Networking.Amf.Common;
using Harmonic.Networking.Amf.Data;
using Harmonic.Networking.Amf.Serialization.Amf3;
using Harmonic.Networking.Amf.Serialization.Attributes;
using Harmonic.Networking.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;

namespace Harmonic.Networking.Amf.Serialization.Amf0
{
    public class Amf0Reader
    {
        public readonly IReadOnlyDictionary<Amf0Type, long> TypeLengthMap = new Dictionary<Amf0Type, long>()
        {
            { Amf0Type.Number, 8 },
            { Amf0Type.Boolean, sizeof(byte) },
            { Amf0Type.String, Amf0CommonValues.STRING_HEADER_LENGTH },
            { Amf0Type.LongString, Amf0CommonValues.LONG_STRING_HEADER_LENGTH },
            { Amf0Type.Object, /* object marker*/ Amf0CommonValues.MARKER_LENGTH - /* utf8-empty */Amf0CommonValues.STRING_HEADER_LENGTH - /* object end marker */Amf0CommonValues.MARKER_LENGTH },
            { Amf0Type.Null, 0 },
            { Amf0Type.Undefined, 0 },
            { Amf0Type.Reference, sizeof(ushort) },
            { Amf0Type.EcmaArray, sizeof(uint) },
            { Amf0Type.StrictArray, sizeof(uint) },
            { Amf0Type.Date, 10 },
            { Amf0Type.Unsupported, 0 },
            { Amf0Type.XmlDocument, 0 },
            { Amf0Type.TypedObject, /* object marker*/ Amf0CommonValues.MARKER_LENGTH - /* class name */ Amf0CommonValues.STRING_HEADER_LENGTH - /* at least on character for class name */ 1 - /* utf8-empty */Amf0CommonValues.STRING_HEADER_LENGTH - /* object end marker */Amf0CommonValues.MARKER_LENGTH },
            { Amf0Type.AvmPlusObject, 0 },
            { Amf0Type.ObjectEnd, 0 }
        };

        private delegate bool ReadDataHandler<T>(Span<byte> buffer, out T data, out int consumedLength);
        private delegate bool ReadDataHandler(Span<byte> buffer, out object data, out int consumedLength);

        private List<Type> _registeredTypes = new List<Type>();
        public IReadOnlyList<Type> RegisteredTypes { get; }
        private IReadOnlyDictionary<Amf0Type, ReadDataHandler> _readDataHandlers;
        private Dictionary<string, TypeRegisterState> _registeredTypeStates = new Dictionary<string, TypeRegisterState>();
        private List<object> _referenceTable = new List<object>();
        private Amf3.Amf3Reader _amf3Reader = new Amf3.Amf3Reader();
        public bool StrictMode { get; set; } = true;

        public Amf0Reader()
        {
            var readDataHandlers = new Dictionary<Amf0Type, ReadDataHandler>
            {
                [Amf0Type.Number] = OutValueTypeEraser<double>(TryGetNumber),
                [Amf0Type.Boolean] = OutValueTypeEraser<bool>(TryGetBoolean),
                [Amf0Type.String] = OutValueTypeEraser<string>(TryGetString),
                [Amf0Type.Object] = OutValueTypeEraser<AmfObject>(TryGetObject),
                [Amf0Type.Null] = OutValueTypeEraser<object>(TryGetNull),
                [Amf0Type.Undefined] = OutValueTypeEraser<Undefined>(TryGetUndefined),
                [Amf0Type.Reference] = OutValueTypeEraser<object>(TryGetReference),
                [Amf0Type.EcmaArray] = OutValueTypeEraser<Dictionary<string, object>>(TryGetEcmaArray),
                [Amf0Type.StrictArray] = OutValueTypeEraser<List<object>>(TryGetStrictArray),
                [Amf0Type.Date] = OutValueTypeEraser<DateTime>(TryGetDate),
                [Amf0Type.LongString] = OutValueTypeEraser<string>(TryGetLongString),
                [Amf0Type.Unsupported] = OutValueTypeEraser<Unsupported>(TryGetUnsupported),
                [Amf0Type.XmlDocument] = OutValueTypeEraser<XmlDocument>(TryGetXmlDocument),
                [Amf0Type.TypedObject] = OutValueTypeEraser<object>(TryGetTypedObject),
                [Amf0Type.AvmPlusObject] = OutValueTypeEraser<object>(TryGetAvmPlusObject)
            };
            _readDataHandlers = readDataHandlers;
        }

        public void RegisterType<T>() where T : new()
        {
            var type = typeof(T);
            var props = type.GetProperties();
            var fields = props.Where(p => p.CanWrite && Attribute.GetCustomAttribute(p, typeof(ClassFieldAttribute)) != null).ToList();
            var members = fields.ToDictionary(p => ((ClassFieldAttribute)Attribute.GetCustomAttribute(p, typeof(ClassFieldAttribute))).Name ?? p.Name, p => new Action<object, object>(p.SetValue));
            if (members.Keys.Where(s => string.IsNullOrEmpty(s)).Any())
            {
                throw new InvalidOperationException("Field name cannot be empty or null");
            }
            string mapedName = null;
            var attr = type.GetCustomAttribute<TypedObjectAttribute>();
            if (attr != null)
            {
                mapedName = attr.Name;
            }
            var typeName = mapedName == null ? type.Name : mapedName;
            var state = new TypeRegisterState()
            {
                Members = members,
                Type = type
            };
            _registeredTypes.Add(type);
            _registeredTypeStates.Add(typeName, state);
            _amf3Reader.RegisterTypedObject(typeName, state);
        }

        public void RegisterIExternalizableForAvmPlus<T>() where T : IExternalizable, new()
        {
            _amf3Reader.RegisterExternalizable<T>();
        }

        private ReadDataHandler OutValueTypeEraser<T>(ReadDataHandler<T> handler)
        {
            return (Span<byte> b, out object d, out int c) =>
            {
                var ret = handler(b, out var n, out c);
                d = n;
                return ret;
            };
        }

        private bool TryReadHeader(Span<byte> buffer, out KeyValuePair<string, object> header, out int consumed)
        {
            header = default;
            consumed = 0;
            if (!TryGetStringImpl(buffer, Amf0.Amf0CommonValues.STRING_HEADER_LENGTH, out var headerName, out var nameConsumed))
            {
                return false;
            }

            buffer = buffer.Slice(nameConsumed);
            if (buffer.Length < 1)
            {
                return false;
            }
            var mustUnderstand = buffer[0];
            buffer = buffer.Slice(1);
            if (buffer.Length < sizeof(uint))
            {
                return false;
            }

            buffer = buffer.Slice(sizeof(uint));
            if (!TryGetValue(buffer, out _, out var headerValue, out var valueConsumed))
            {
                return false;
            }
            header = new KeyValuePair<string, object>(headerName, headerValue);
            consumed = nameConsumed + 1 + sizeof(uint) + valueConsumed;
            return true;
        }

        public bool TryGetMessage(Span<byte> buffer, out Message message, out int consumed)
        {
            message = default;
            consumed = default;

            if (!TryGetStringImpl(buffer, Amf0CommonValues.STRING_HEADER_LENGTH, out var targetUri, out var targetUriConsumed))
            {
                return false;
            }

            buffer = buffer.Slice(targetUriConsumed);
            if (!TryGetStringImpl(buffer, Amf0CommonValues.STRING_HEADER_LENGTH, out var responseUri, out var responseUriConsumed))
            {
                return false;
            }

            buffer = buffer.Slice(responseUriConsumed);
            if (buffer.Length < sizeof(uint))
            {
                return false;
            }
            var messageLength = NetworkBitConverter.ToUInt32(buffer);
            if (messageLength >= 0 && buffer.Length < messageLength)
            {
                return false;
            }
            if (messageLength == 0 && StrictMode)
            {
                return true;
            }
            buffer = buffer.Slice(sizeof(uint));
            if (!TryGetValue(buffer, out _, out var content, out var contentConsumed))
            {
                return false;
            }
            buffer = buffer.Slice(contentConsumed);
            consumed = targetUriConsumed + responseUriConsumed + sizeof(uint) + contentConsumed;
            message = new Message()
            {
                TargetUri = targetUri,
                ResponseUri = responseUri,
                Content = content
            };
            return true;
        }

        public bool TryGetPacket(Span<byte> buffer, out List<KeyValuePair<string, object>> headers, out List<Message> messages, out int consumed)
        {
            headers = default;
            messages = default;
            consumed = 0;

            if (buffer.Length < 1)
            {
                return false;
            }
            var version = NetworkBitConverter.ToUInt16(buffer);
            buffer = buffer.Slice(sizeof(ushort));
            consumed += sizeof(ushort);
            var headerCount = NetworkBitConverter.ToUInt16(buffer);
            buffer = buffer.Slice(sizeof(ushort));
            consumed += sizeof(ushort);
            headers = new List<KeyValuePair<string, object>>();
            messages = new List<Message>();
            for (int i = 0; i < headerCount; i++)
            {
                if (!TryReadHeader(buffer, out var header, out var headerConsumed))
                {
                    return false;
                }
                headers.Add(header);
                buffer = buffer.Slice(headerConsumed);
                consumed += headerConsumed;
            }

            var messageCount = NetworkBitConverter.ToUInt16(buffer);
            buffer = buffer.Slice(sizeof(ushort));
            consumed += sizeof(ushort);
            for (int i = 0; i < messageCount; i++)
            {
                if (!TryGetMessage(buffer, out var message, out var messageConsumed))
                {
                    return false;
                }
                messages.Add(message);
                consumed += messageConsumed;
            }
            return true;
        }

        public bool TryDescribeData(Span<byte> buffer, out Amf0Type type, out int consumedLength)
        {
            type = default;
            consumedLength = default;
            if (buffer.Length < Amf0CommonValues.MARKER_LENGTH)
            {
                return false;
            }

            var marker = (Amf0Type)buffer[0];
            if (!TypeLengthMap.TryGetValue(marker, out var bytesNeed))
            {
                return false;
            }
            if (buffer.Length - Amf0CommonValues.MARKER_LENGTH < bytesNeed)
            {
                return false;
            }

            type = marker;
            consumedLength = (int)bytesNeed + Amf0CommonValues.MARKER_LENGTH;

            return true;
        }

        public bool TryGetNumber(Span<byte> buffer, out double value, out int bytesConsumed)
        {
            value = default;
            bytesConsumed = default;
            if (!TryDescribeData(buffer, out var type, out var length))
            {
                return false;
            }
            if (type != Amf0Type.Number)
            {
                return false;
            }
            value = NetworkBitConverter.ToDouble(buffer.Slice(Amf0CommonValues.MARKER_LENGTH));
            bytesConsumed = length;
            return true;
        }

        public bool TryGetBoolean(Span<byte> buffer, out bool value, out int bytesConsumed)
        {
            value = default;
            bytesConsumed = default;

            if (!TryDescribeData(buffer, out var type, out var length))
            {
                return false;
            }

            if (type != Amf0Type.Boolean)
            {
                return false;
            }

            value = buffer[1] != 0;
            bytesConsumed = length;
            return true;
        }

        public bool TryGetString(Span<byte> buffer, out string value, out int bytesConsumed)
        {
            value = default;
            bytesConsumed = default;

            if (!TryDescribeData(buffer, out var type, out _))
            {
                return false;
            }

            if (type != Amf0Type.String)
            {
                return false;
            }


            if (!TryGetStringImpl(buffer.Slice(Amf0CommonValues.MARKER_LENGTH), Amf0CommonValues.STRING_HEADER_LENGTH, out value, out bytesConsumed))
            {
                return false;
            }

            bytesConsumed += Amf0CommonValues.MARKER_LENGTH;
            _referenceTable.Add(value);
            return true;
        }

        private bool TryGetObjectImpl(Span<byte> objectBuffer, out Dictionary<string, object> value, out int bytesConsumed)
        {
            value = default;
            bytesConsumed = default;
            var obj = new Dictionary<string, object>();
            _referenceTable.Add(obj);
            var consumed = 0;
            while (true)
            {
                if (!TryGetStringImpl(objectBuffer, Amf0CommonValues.STRING_HEADER_LENGTH, out var key, out var keyLength))
                {
                    return false;
                }
                consumed += keyLength;
                objectBuffer = objectBuffer.Slice(keyLength);

                if (!TryGetValue(objectBuffer, out var dataType, out var data, out var valueLength))
                {
                    return false;
                }
                consumed += valueLength;
                objectBuffer = objectBuffer.Slice(valueLength);

                if (!key.Any() && dataType == Amf0Type.ObjectEnd)
                {
                    break;
                }
                obj.Add(key, data);
            }
            value = obj;
            bytesConsumed = consumed;
            return true;
        }

        public bool TryGetObject(Span<byte> buffer, out AmfObject value, out int bytesConsumed)
        {
            value = default;
            bytesConsumed = default;

            if (!TryDescribeData(buffer, out var type, out _))
            {
                return false;
            }

            if (type == Amf0Type.Null)
            {
                if (!TryGetNull(buffer, out _, out bytesConsumed))
                {
                    return false;
                }
                value = null;
                return true;
            }

            if (type != Amf0Type.Object)
            {
                return false;
            }

            var objectBuffer = buffer.Slice(Amf0CommonValues.MARKER_LENGTH);

            if (!TryGetObjectImpl(objectBuffer, out var obj, out var consumed))
            {
                return false;
            }

            value = new AmfObject(obj);
            bytesConsumed = consumed + Amf0CommonValues.MARKER_LENGTH;


            return true;
        }

        public bool TryGetNull(Span<byte> buffer, out object value, out int bytesConsumed)
        {
            value = default;
            bytesConsumed = default;
            if (!TryDescribeData(buffer, out var type, out var length))
            {
                return false;
            }

            if (type != Amf0Type.Null)
            {
                return false;
            }
            value = null;
            bytesConsumed = Amf0CommonValues.MARKER_LENGTH;
            return true;
        }

        public bool TryGetUndefined(Span<byte> buffer, out Undefined value, out int consumedLength)
        {
            value = default;
            consumedLength = default;
            if (!TryDescribeData(buffer, out var type, out var length))
            {
                return false;
            }

            if (type != Amf0Type.Undefined)
            {
                return false;
            }
            value = new Undefined();
            consumedLength = Amf0CommonValues.MARKER_LENGTH;
            return true;
        }

        private bool TryGetReference(Span<byte> buffer, out object value, out int consumedLength)
        {
            var index = 0;
            value = default;
            consumedLength = default;
            if (!TryDescribeData(buffer, out var type, out var length))
            {
                return false;
            }

            if (type != Amf0Type.Reference)
            {
                return false;
            }

            index = NetworkBitConverter.ToUInt16(buffer.Slice(Amf0CommonValues.MARKER_LENGTH, sizeof(ushort)));
            consumedLength = Amf0CommonValues.MARKER_LENGTH + sizeof(ushort);
            if (_referenceTable.Count <= index)
            {
                return false;
            }
            value = _referenceTable[index];
            return true;
        }

        private bool TryGetKeyValuePair(Span<byte> buffer, out KeyValuePair<string, object> value, out bool kvEnd, out int consumed)
        {
            value = default;
            kvEnd = default;

            consumed = 0;
            if (!TryGetStringImpl(buffer, Amf0CommonValues.STRING_HEADER_LENGTH, out var key, out var keyLength))
            {
                return false;
            }
            consumed += keyLength;
            if (buffer.Length - keyLength < 0)
            {
                return false;
            }
            buffer = buffer.Slice(keyLength);

            if (!TryGetValue(buffer, out var elementType, out var element, out var valueLength))
            {
                return false;
            }
            consumed += valueLength;
            value = new KeyValuePair<string, object>(key, element);
            kvEnd = !key.Any() && elementType == Amf0Type.ObjectEnd;

            return true;
        }

        public bool TryGetEcmaArray(Span<byte> buffer, out Dictionary<string, object> value, out int consumedLength)
        {
            value = default;
            consumedLength = default;
            int consumed = 0;

            if (!TryDescribeData(buffer, out var type, out _))
            {
                return false;
            }

            if (type != Amf0Type.EcmaArray)
            {
                return false;
            }

            var obj = new Dictionary<string, object>();
            _referenceTable.Add(obj);

            var elementCount = NetworkBitConverter.ToUInt32(buffer.Slice(Amf0CommonValues.MARKER_LENGTH, sizeof(uint)));

            var arrayBodyBuffer = buffer.Slice(Amf0CommonValues.MARKER_LENGTH + sizeof(uint));
            consumed = Amf0CommonValues.MARKER_LENGTH + sizeof(uint);
            if (StrictMode)
            {
                for (int i = 0; i < elementCount; i++)
                {
                    if (!TryGetKeyValuePair(arrayBodyBuffer, out var kv, out _, out var kvConsumed))
                    {
                        return false;
                    }
                    arrayBodyBuffer = arrayBodyBuffer.Slice(kvConsumed);
                    consumed += kvConsumed;
                    obj.Add(kv.Key, kv.Value);
                }
                if (!TryGetStringImpl(arrayBodyBuffer, Amf0CommonValues.STRING_HEADER_LENGTH, out var emptyStr, out var emptyStrConsumed))
                {
                    return false;
                }
                if (emptyStr.Any())
                {
                    return false;
                }
                consumed += emptyStrConsumed;
                arrayBodyBuffer = arrayBodyBuffer.Slice(emptyStrConsumed);
                if (!TryDescribeData(arrayBodyBuffer, out var objEndType, out var objEndConsumed))
                {
                    return false;
                }
                if (objEndType != Amf0Type.ObjectEnd)
                {
                    return false;
                }
                consumed += objEndConsumed;
            }
            else
            {
                while (true)
                {
                    if (!TryGetKeyValuePair(arrayBodyBuffer, out var kv, out var isEnd, out var kvConsumed))
                    {
                        return false;
                    }
                    arrayBodyBuffer = arrayBodyBuffer.Slice(kvConsumed);
                    consumed += kvConsumed;
                    if (isEnd)
                    {
                        break;
                    }
                    obj.Add(kv.Key, kv.Value);
                }
            }


            value = obj;
            consumedLength = consumed;
            return true;
        }

        public bool TryGetStrictArray(Span<byte> buffer, out List<object> array, out int consumedLength)
        {
            array = default;
            consumedLength = default;

            if (!TryDescribeData(buffer, out var type, out _))
            {
                return false;
            }

            if (type != Amf0Type.StrictArray)
            {
                return false;
            }

            var obj = new List<object>();
            _referenceTable.Add(obj);

            var elementCount = NetworkBitConverter.ToUInt32(buffer.Slice(Amf0CommonValues.MARKER_LENGTH, sizeof(uint)));

            int consumed = Amf0CommonValues.MARKER_LENGTH + sizeof(uint);
            var arrayBodyBuffer = buffer.Slice(consumed);
            var elementBodyBuffer = arrayBodyBuffer;
            for (uint i = 0; i < elementCount; i++)
            {
                if (!TryGetValue(elementBodyBuffer, out _, out var element, out var bufferConsumed))
                {
                    return false;
                }

                obj.Add(element);
                if (elementBodyBuffer.Length - bufferConsumed < 0)
                {
                    return false;
                }
                elementBodyBuffer = elementBodyBuffer.Slice(bufferConsumed);
                consumed += bufferConsumed;
            }
            array = obj;
            consumedLength = consumed;

            return true;
        }

        public bool TryGetDate(Span<byte> buffer, out DateTime value, out int consumendLength)
        {
            value = default;
            consumendLength = default;

            if (!TryDescribeData(buffer, out var type, out var length))
            {
                return false;
            }

            if (type != Amf0Type.Date)
            {
                return false;
            }

            var timestamp = NetworkBitConverter.ToDouble(buffer.Slice(Amf0CommonValues.MARKER_LENGTH));
            value = DateTimeOffset.FromUnixTimeMilliseconds((long)timestamp).LocalDateTime;
            consumendLength = length;
            return true;
        }

        public bool TryGetLongString(Span<byte> buffer, out string value, out int consumedLength)
        {
            value = default;
            consumedLength = default;

            if (!TryDescribeData(buffer, out var type, out _))
            {
                return false;
            }

            if (type != Amf0Type.LongString)
            {
                return false;
            }

            if (!TryGetStringImpl(buffer.Slice(Amf0CommonValues.MARKER_LENGTH), Amf0CommonValues.LONG_STRING_HEADER_LENGTH, out value, out consumedLength))
            {
                return false;
            }

            consumedLength += Amf0CommonValues.MARKER_LENGTH;

            return true;
        }

        internal bool TryGetStringImpl(Span<byte> buffer, int lengthOfLengthField, out string value, out int consumedLength)
        {
            value = default;
            consumedLength = default;
            var stringLength = 0;
            if (lengthOfLengthField == Amf0CommonValues.STRING_HEADER_LENGTH)
            {
                stringLength = (int)NetworkBitConverter.ToUInt16(buffer);
            }
            else
            {
                stringLength = (int)NetworkBitConverter.ToUInt32(buffer);
            }

            if (buffer.Length - lengthOfLengthField < stringLength)
            {
                return false;
            }

            value = Encoding.UTF8.GetString(buffer.Slice(lengthOfLengthField, stringLength));
            consumedLength = lengthOfLengthField + stringLength;
            return true;
        }

        private bool TryGetUnsupported(Span<byte> buffer, out Unsupported value, out int consumedLength)
        {
            value = default;
            consumedLength = default;

            if (!TryDescribeData(buffer, out var type, out var length))
            {
                return false;
            }

            if (type != Amf0Type.Unsupported)
            {
                return false;
            }

            value = new Unsupported();
            consumedLength = Amf0CommonValues.MARKER_LENGTH;

            return true;
        }

        public bool TryGetXmlDocument(Span<byte> buffer, out XmlDocument value, out int consumedLength)
        {
            value = default;
            consumedLength = default;
            if (!TryDescribeData(buffer, out var type, out _))
            {
                return false;
            }

            if (!TryGetStringImpl(buffer.Slice(Amf0CommonValues.MARKER_LENGTH), Amf0CommonValues.LONG_STRING_HEADER_LENGTH, out var str, out consumedLength))
            {
                return false;
            }

            value = new XmlDocument();
            value.LoadXml(str);
            consumedLength += Amf0CommonValues.MARKER_LENGTH;

            return true;
        }

        public bool TryGetTypedObject(Span<byte> buffer, out object value, out int consumedLength)
        {
            value = default;
            consumedLength = default;

            if (!TryDescribeData(buffer, out var type, out _))
            {
                return true;
            }

            if (type != Amf0Type.TypedObject)
            {
                return false;
            }

            var consumed = Amf0CommonValues.MARKER_LENGTH;

            if (!TryGetStringImpl(buffer.Slice(Amf0CommonValues.MARKER_LENGTH), Amf0CommonValues.STRING_HEADER_LENGTH, out var className, out var stringLength))
            {
                return false;
            }

            consumed += stringLength;

            var objectBuffer = buffer.Slice(consumed);

            if (!TryGetObjectImpl(objectBuffer, out var dict, out var objectConsumed))
            {
                return false;
            }

            consumed += objectConsumed;

            if (!_registeredTypeStates.TryGetValue(className, out var state))
            {
                return false;
            }
            var objectType = state.Type;
            var obj = Activator.CreateInstance(objectType);

            if (state.Members.Keys.Except(dict.Keys).Any())
            {
                return false;
            }
            else if (dict.Keys.Except(state.Members.Keys).Any())
            {
                return false;
            }

            foreach ((var name, var setter) in state.Members)
            {
                setter(obj, dict[name]);
            }

            value = obj;
            consumedLength = consumed;

            return true;
        }

        public bool TryGetAvmPlusObject(Span<byte> buffer, out object value, out int consumed)
        {
            value = default;
            consumed = default;

            if (!TryDescribeData(buffer, out var type, out _))
            {
                return false;
            }
            if (type != Amf0Type.AvmPlusObject)
            {
                return false;
            }

            buffer = buffer.Slice(Amf0CommonValues.MARKER_LENGTH);

            if (!_amf3Reader.TryGetValue(buffer, out value, out consumed))
            {
                return false;
            }

            consumed += Amf0CommonValues.MARKER_LENGTH;

            return true;
        }

        public bool TryGetValue(Span<byte> objectBuffer, out Amf0Type objectType, out object data, out int valueLength)
        {
            data = default;
            valueLength = default;
            objectType = default;
            if (!TryDescribeData(objectBuffer, out var type, out var length))
            {
                return false;
            }

            if (type == Amf0Type.ObjectEnd)
            {
                objectType = type;
                valueLength = Amf0CommonValues.MARKER_LENGTH;
                return true;
            }

            if (!_readDataHandlers.TryGetValue(type, out var handler))
            {
                return false;
            }

            if (!handler(objectBuffer, out data, out valueLength))
            {
                return false;
            }
            objectType = type;
            return true;
        }
    }
}
