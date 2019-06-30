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
    public class Amf0Reader
    {
        public readonly static IReadOnlyDictionary<Amf0Type, long> TypeLengthMap = new Dictionary<Amf0Type, long>()
        {
            { Amf0Type.Number, 8 },
            { Amf0Type.Boolean, sizeof(byte) },
            { Amf0Type.String, Amf0CommonValues.STRING_HEADER_LENGTH },
            { Amf0Type.Object, /* object marker*/ Amf0CommonValues.MARKER_LENGTH - /* utf8-empty */Amf0CommonValues.STRING_HEADER_LENGTH - /* object end marker */Amf0CommonValues.MARKER_LENGTH },
            { Amf0Type.Null, 0 },
            { Amf0Type.Undefined, 0 },
            { Amf0Type.Reference, sizeof(ushort) },
            { Amf0Type.EcmaArray, sizeof(uint) },
            { Amf0Type.StrictArray, sizeof(uint) },
            { Amf0Type.Date, 10 },
            { Amf0Type.String, Amf0CommonValues.STRING_HEADER_LENGTH },
            { Amf0Type.Unsupported, 0 },
            { Amf0Type.XmlDocument, Amf0CommonValues.STRING_HEADER_LENGTH },
            { Amf0Type.TypedObject, /* object marker*/ Amf0CommonValues.MARKER_LENGTH - /* class name */ Amf0CommonValues.STRING_HEADER_LENGTH - /* at least on character for class name */ 1 - /* utf8-empty */Amf0CommonValues.STRING_HEADER_LENGTH - /* object end marker */Amf0CommonValues.MARKER_LENGTH }
        };

        private delegate bool ReadDataHandler<T>(Span<byte> buffer, out T data, out int consumedLength);
        private delegate bool ReadDataHandler(Span<byte> buffer, out object data, out int consumedLength);

        public IReadOnlyDictionary<string, Type> RegisteredTypes => _registeredTypes;
        private IReadOnlyDictionary<Amf0Type, ReadDataHandler> _readDataHandlers;
        private Dictionary<string, Type> _registeredTypes = new Dictionary<string, Type>();
        private List<object> _referenceTable = new List<object>();

        public Amf0Reader()
        {
            var readDataHandlers = new Dictionary<Amf0Type, ReadDataHandler>();
            readDataHandlers[Amf0Type.Number] = OutValueTypeEraser<double>(TryGetNumber);
            readDataHandlers[Amf0Type.Boolean] = OutValueTypeEraser<bool>(TryGetBoolean);
            readDataHandlers[Amf0Type.String] = OutValueTypeEraser<string>(TryGetString);
            readDataHandlers[Amf0Type.Object] = OutValueTypeEraser<Dictionary<string, object>>(TryGetObject);
            readDataHandlers[Amf0Type.Null] = OutValueTypeEraser<object>(TryGetNull);
            readDataHandlers[Amf0Type.Undefined] = OutValueTypeEraser<Undefined>(TryGetUndefined);
            readDataHandlers[Amf0Type.Reference] = OutValueTypeEraser<object>(TryGetReference);
            readDataHandlers[Amf0Type.EcmaArray] = OutValueTypeEraser<Dictionary<string, object>>(TryGetEcmaArray);
            readDataHandlers[Amf0Type.StrictArray] = OutValueTypeEraser<List<object>>(TryGetStrictArray);
            readDataHandlers[Amf0Type.Date] = OutValueTypeEraser<DateTime>(TryGetDate);
            readDataHandlers[Amf0Type.LongString] = OutValueTypeEraser<string>(TryGetLongString);
            readDataHandlers[Amf0Type.Unsupported] = OutValueTypeEraser<Unsupported>(TryGetUnsupported);
            readDataHandlers[Amf0Type.XmlDocument] = OutValueTypeEraser<XmlDocument>(TryGetXmlDocument);
            readDataHandlers[Amf0Type.TypedObject] = OutValueTypeEraser<object>(TryGetTypedObject);
            _readDataHandlers = readDataHandlers;
        }
        public void RegisterType<T>()
        {
            _registeredTypes.Add(typeof(T).Name, typeof(T));
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
            value = RtmpBitConverter.ToDouble(buffer.Slice(Amf0CommonValues.MARKER_LENGTH));
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

            return true;
        }

        private bool TryGetObjectImpl(Span<byte> objectBuffer, out Dictionary<string, object> value, out int bytesConsumed)
        {
            value = default;
            bytesConsumed = default;
            var obj = new Dictionary<string, object>();
            var consumed = Amf0CommonValues.MARKER_LENGTH;
            while (true)
            {
                if (!TryGetString(objectBuffer, out var key, out var keyLength))
                {
                    return false;
                }
                consumed += keyLength;

                if (objectBuffer.Length - keyLength < 0)
                {
                    return false;
                }
                objectBuffer = objectBuffer.Slice(keyLength);

                if (!TryGetValue(objectBuffer, out var dataType, out var data, out var valueLength))
                {
                    return false;
                }
                consumed += valueLength;

                if (objectBuffer.Length - valueLength < 0)
                {
                    return false;
                }
                objectBuffer = objectBuffer.Slice(valueLength);
                if (!key.Any() && dataType == Amf0Type.ObjectEnd)
                {
                    break;
                }
                obj.Add(key, data);
            }
            return true;
        }

        public bool TryGetObject(Span<byte> buffer, out Dictionary<string, object> value, out int bytesConsumed)
        {
            value = default;
            bytesConsumed = default;

            if (!TryDescribeData(buffer, out var type, out _))
            {
                return false;
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

            value = obj;
            bytesConsumed = consumed;

            _referenceTable.Add(value);

            return true;
        }

        private bool TryGetNull(Span<byte> buffer, out object value, out int bytesConsumed)
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

        private bool TryGetUndefined(Span<byte> buffer, out Undefined value, out int consumedLength)
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

            index = RtmpBitConverter.ToUInt16(buffer.Slice(Amf0CommonValues.MARKER_LENGTH, sizeof(ushort)));
            consumedLength = Amf0CommonValues.MARKER_LENGTH + sizeof(ushort);
            if (_referenceTable.Count <= index)
            {
                return false;
            }
            value = _referenceTable[index];
            return true;
        }

        private bool TryGetEcmaArray(Span<byte> buffer, out Dictionary<string, object> value, out int consumedLength)
        {
            value = default;
            consumedLength = default;

            if (!TryDescribeData(buffer, out var type, out _))
            {
                return false;
            }

            if (type != Amf0Type.EcmaArray)
            {
                return false;
            }

            var obj = new Dictionary<string, object>();

            var elementCount = RtmpBitConverter.ToUInt32(buffer.Slice(Amf0CommonValues.MARKER_LENGTH, sizeof(uint)));

            var arrayBodyBuffer = buffer.Slice(Amf0CommonValues.MARKER_LENGTH + sizeof(uint));
            var elementBodyBuffer = arrayBodyBuffer;
            int consumed = 0;
            for (uint i = 0; i < elementCount; i++)
            {
                if (!TryGetString(arrayBodyBuffer, out var key, out var keyLength))
                {
                    return false;
                }
                consumed += keyLength;
                if (elementBodyBuffer.Length - keyLength < 0)
                {
                    return false;
                }
                elementBodyBuffer = elementBodyBuffer.Slice(keyLength);

                if (!TryGetValue(elementBodyBuffer, out _, out var element, out var valueLength))
                {
                    return false;
                }
                consumed += valueLength;

                obj.Add(key, element);
                if (elementBodyBuffer.Length - valueLength < 0)
                {
                    return false;
                }
                elementBodyBuffer = elementBodyBuffer.Slice(valueLength);
            }
            value = obj;
            consumedLength = consumed;
            _referenceTable.Add(value);
            return true;
        }

        private bool TryGetStrictArray(Span<byte> buffer, out List<object> array, out int consumedLength)
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

            var elementCount = RtmpBitConverter.ToUInt32(buffer.Slice(Amf0CommonValues.MARKER_LENGTH, sizeof(uint)));

            var arrayBodyBuffer = buffer.Slice(Amf0CommonValues.MARKER_LENGTH + sizeof(uint));
            var elementBodyBuffer = arrayBodyBuffer;
            int consumed = 0;
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

            _referenceTable.Add(array);
            return true;
        }

        private bool TryGetDate(Span<byte> buffer, out DateTime value, out int consumendLength)
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

            var timestamp = RtmpBitConverter.ToDouble(buffer.Slice(Amf0CommonValues.MARKER_LENGTH));
            value = DateTimeOffset.FromUnixTimeMilliseconds((long)(timestamp * 1000)).DateTime;
            consumendLength = length;
            return true;
        }

        private bool TryGetLongString(Span<byte> buffer, out string value, out int consumedLength)
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

            if (!TryGetStringImpl(buffer.Slice(Amf0CommonValues.MARKER_LENGTH), Amf0CommonValues.STRING_HEADER_LENGTH, out value, out consumedLength))
            {
                return false;
            }

            consumedLength += Amf0CommonValues.MARKER_LENGTH;

            return true;
        }

        private bool TryGetStringImpl(Span<byte> buffer, int lengthOfLengthField, out string value, out int consumedLength)
        {
            value = default;
            consumedLength = default;
            var stringLength = (int)RtmpBitConverter.ToUInt32(buffer);
            if (buffer.Length - Amf0CommonValues.STRING_HEADER_LENGTH < stringLength)
            {
                return false;
            }

            value = Encoding.UTF8.GetString(buffer.Slice(Amf0CommonValues.STRING_HEADER_LENGTH, stringLength));
            consumedLength = Amf0CommonValues.STRING_HEADER_LENGTH + stringLength;
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

        private bool TryGetXmlDocument(Span<byte> buffer, out XmlDocument value, out int consumedLength)
        {
            value = default;
            consumedLength = default;
            if (!TryDescribeData(buffer, out var type, out _))
            {
                return false;
            }

            if (!TryGetStringImpl(buffer.Slice(Amf0CommonValues.MARKER_LENGTH), Amf0CommonValues.STRING_HEADER_LENGTH, out var str, out consumedLength))
            {
                return false;
            }

            value = new XmlDocument();
            value.LoadXml(str);
            consumedLength += Amf0CommonValues.MARKER_LENGTH;

            return true;
        }

        private bool TryGetTypedObject(Span<byte> buffer, out object value, out int consumedLength)
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

            var objectTypes = RegisteredTypes.Where(kv => kv.Key == className).ToList();
            if (objectTypes.Count != 1)
            {
                return false;
            }
            var objectType = objectTypes.First().Value;
            var obj = Activator.CreateInstance(objectType);
            var props = objectType.GetProperties();

            if (props.Select(p => p.Name).Except(dict.Keys).Any())
            {
                return false;
            }
            else if (dict.Keys.Except(props.Select(p => p.Name)).Any())
            {
                return false;
            }

            foreach (var prop in props)
            {
                prop.SetValue(obj, dict[prop.Name]);
            }

            value = obj;
            consumedLength = consumed;
            _referenceTable.Add(value);

            return true;
        }

        private bool TryGetValue(Span<byte> objectBuffer, out Amf0Type objectType, out object data, out int valueLength)
        {
            data = default;
            valueLength = default;
            objectType = default;
            if (!TryDescribeData(objectBuffer, out var type, out var length))
            {
                return false;
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
