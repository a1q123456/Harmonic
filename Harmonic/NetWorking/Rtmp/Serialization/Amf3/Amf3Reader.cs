using Harmonic.NetWorking.Rtmp.Common;
using Harmonic.NetWorking.Rtmp.Data;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace Harmonic.NetWorking.Rtmp.Serialization.Amf3
{
    public class Amf3Reader
    {
        private delegate bool ReaderHandler<T>(Span<byte> buffer, out T value, out int consumed);
        private delegate bool ReaderHandler(Span<byte> buffer, out object value, out int consumed);

        private List<object> _readObjectReferenceTable = new List<object>();
        private List<string> _readStringReferenceTable = new List<string>();
        private List<Amf3ClassTraits> _readObjectTraitsReferenceTable = new List<Amf3ClassTraits>();
        private Dictionary<Amf3Type, ReaderHandler> _readerHandlers = new Dictionary<Amf3Type, ReaderHandler>();
        private Dictionary<string, Type> _registeredTypedObeject = new Dictionary<string, Type>();
        private Dictionary<string, Type> _registeredExternalizable = new Dictionary<string, Type>();
        private readonly IReadOnlyList<Amf3Type> _supportedTypes = null;

        public Amf3Reader()
        {
            var supportedTypes = new List<Amf3Type>()
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
            _supportedTypes = supportedTypes;

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
        }

        private ReaderHandler ReaderHandlerWrapper<T>(ReaderHandler<T> handler)
        {
            return (Span<byte> b, out object value, out int consumed) =>
            {
                value = default;
                consumed = default;

                if (handler(b, out var data, out consumed))
                {
                    value = data;
                    return true;
                }
                return false;
            };
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


        public bool TryDescribeData(Span<byte> buffer, out Amf3Type type)
        {
            type = default;
            if (buffer.Length < Amf3CommonValues.MARKER_LENGTH)
            {
                return false;
            }

            var typeMark = (Amf3Type)buffer[0];
            if (_supportedTypes.Contains(typeMark))
            {
                return false;
            }

            type = typeMark;
            return true;
        }

        public bool DataIsType(Span<byte> buffer, Amf3Type type)
        {
            if (!TryDescribeData(buffer, out var dataType))
            {
                return false;
            }
            return dataType == type;
        }

        public bool TryGetUndefined(Span<byte> buffer, out Undefined value, out int consumed)
        {
            value = default;
            consumed = default;
            if (!DataIsType(buffer, Amf3Type.Undefined))
            {
                return false;
            }

            value = new Undefined();
            consumed = Amf3CommonValues.MARKER_LENGTH;
            return true;
        }

        public bool TryGetNull(Span<byte> buffer, out object value, out int consumed)
        {
            value = default;
            consumed = default;
            if (!DataIsType(buffer, Amf3Type.Null))
            {
                return false;
            }

            value = null;
            consumed = Amf3CommonValues.MARKER_LENGTH;
            return true;
        }

        public bool TryGetTrue(Span<byte> buffer, out bool value, out int consumed)
        {
            value = default;
            consumed = default;
            if (!DataIsType(buffer, Amf3Type.True))
            {
                return false;
            }

            value = true;
            consumed = Amf3CommonValues.MARKER_LENGTH;
            return true;
        }

        public bool TryGetFalse(Span<byte> buffer, out bool value, out int consumed)
        {
            value = default;
            consumed = default;
            if (!DataIsType(buffer, Amf3Type.False))
            {
                return false;
            }

            value = false;
            consumed = Amf3CommonValues.MARKER_LENGTH;
            return true;
        }

        public bool TryGetUInt29(Span<byte> buffer, out uint value, out int consumed)
        {
            value = default;
            consumed = default;
            if (!DataIsType(buffer, Amf3Type.Integer))
            {
                return false;
            }

            var dataBuffer = buffer.Slice(Amf3CommonValues.MARKER_LENGTH);

            if (!TryGetU29Impl(dataBuffer, out value, out var dataConsumed))
            {
                return false;
            }

            consumed = Amf3CommonValues.MARKER_LENGTH + dataConsumed;

            return true;
        }

        private bool TryGetU29Impl(Span<byte> dataBuffer, out uint value, out int consumed)
        {
            value = default;
            consumed = default;
            var bytesNeed = 0;
            for (int i = 0; i <= 3; i++)
            {
                bytesNeed++;
                if (dataBuffer.Length < bytesNeed)
                {
                    return false;
                }
                var hasBytes = ((dataBuffer[i] >> 7 & 0x01) == 0x01);
                if (!hasBytes)
                {
                    break;
                }
            }

            byte tmp = 0;
            var maxProcessLength = Math.Min(bytesNeed, 3);
            for (int i = 0; i < maxProcessLength; i++)
            {
                var lowBits = maxProcessLength - 1 - i;
                var mask = (byte)Math.Pow(2, lowBits) - 1;
                var localTmp = (byte)(dataBuffer[i] & mask);
                dataBuffer[i] = (byte)(dataBuffer[i] >> lowBits);
                var highBits = lowBits + 1;
                mask = ~((byte)Math.Pow(2, highBits) - 1);
                dataBuffer[i] = (byte)(dataBuffer[i] & mask);

                var tmpBits = lowBits + 1;
                tmp = (byte)(tmp << (8 - tmpBits));

                dataBuffer[i] = (byte)(dataBuffer[i] | tmp);

                tmp = localTmp;
            }
            value = RtmpBitConverter.ToUInt32(dataBuffer);
            consumed = bytesNeed;
            return true;
        }

        public bool TryGetDouble(Span<byte> buffer, out double value, out int consumed)
        {
            value = default;
            consumed = default;
            if (!DataIsType(buffer, Amf3Type.Double))
            {
                return false;
            }

            value = RtmpBitConverter.ToDouble(buffer.Slice(Amf3CommonValues.MARKER_LENGTH));
            consumed = Amf3CommonValues.MARKER_LENGTH + sizeof(double);
            return true;
        }

        public bool TryGetString(Span<byte> buffer, out string value, out int consumed)
        {
            value = default;
            consumed = default;
            if (!DataIsType(buffer, Amf3Type.String))
            {
                return false;
            }

            var objectBuffer = buffer.Slice(Amf3CommonValues.MARKER_LENGTH);
            TryGetStringImpl(objectBuffer, _readStringReferenceTable, out var str, out var strConsumed);
            value = str;
            consumed = Amf3CommonValues.MARKER_LENGTH + strConsumed;
            return true;
        }

        private bool TryGetStringImpl<T>(Span<byte> objectBuffer, List<T> referenceTable, out string value, out int consumed) where T : class
        {
            value = default;
            consumed = default;
            if (!TryGetU29Impl(objectBuffer, out var header, out int headerLen))
            {
                return false;
            }
            if (!TryGetReference(header, _readStringReferenceTable, out var headerData, out string refValue, out var isRef))
            {
                return false;
            }
            if (isRef)
            {
                value = refValue;
                consumed = Amf3CommonValues.MARKER_LENGTH + headerLen;
            }

            var strLen = (int)headerData;
            if (objectBuffer.Length - headerLen < strLen)
            {
                return false;
            }

            value = Encoding.UTF8.GetString(objectBuffer.Slice(headerLen, strLen));
            consumed = Amf3CommonValues.MARKER_LENGTH + headerLen + strLen;
            referenceTable.Add(value as T);
            return true;
        }

        public bool TryGetXmlDocument(Span<byte> buffer, out XmlDocument value, out int consumed)
        {
            value = default;
            consumed = default;
            if (!DataIsType(buffer, Amf3Type.XmlDocument))
            {
                return false;
            }

            var objectBuffer = buffer.Slice(Amf3CommonValues.MARKER_LENGTH);
            TryGetStringImpl(objectBuffer, _readObjectReferenceTable, out var str, out var strConsumed);
            var xml = new XmlDocument();
            xml.LoadXml(str);
            value = xml;
            consumed = Amf3CommonValues.MARKER_LENGTH + strConsumed;
            return true;
        }

        public bool TryGetDate(Span<byte> buffer, out DateTime value, out int consumed)
        {
            value = default;
            consumed = default;
            if (!DataIsType(buffer, Amf3Type.Date))
            {
                return false;
            }

            var objectBuffer = buffer.Slice(Amf3CommonValues.MARKER_LENGTH);

            if (!TryGetU29Impl(objectBuffer, out var header, out var headerLength))
            {
                return false;
            }
            if (!TryGetReference(header, _readObjectReferenceTable, out var headerData, out DateTime refValue, out var isRef))
            {
                return false;
            }
            if (isRef)
            {
                value = refValue;
                consumed = Amf3CommonValues.MARKER_LENGTH + headerLength;
            }

            var timestamp = RtmpBitConverter.ToDouble(objectBuffer.Slice(headerLength));
            value = DateTimeOffset.FromUnixTimeMilliseconds((long)(timestamp * 1000)).DateTime;
            consumed = Amf3CommonValues.MARKER_LENGTH + headerLength + sizeof(double);
            _readObjectReferenceTable.Add(value);
            return true;
        }

        public bool TryGetArray(Span<byte> buffer, out Dictionary<string, object> value, out int consumed)
        {
            value = default;
            consumed = default;
            if (!DataIsType(buffer, Amf3Type.Array))
            {
                return false;
            }

            if (!TryGetU29Impl(buffer.Slice(Amf3CommonValues.MARKER_LENGTH), out var header, out var headerConsumed))
            {
                return false;
            }

            if (!TryGetReference(header, _readObjectReferenceTable, out var headerData, out Dictionary<string, object> refValue, out var isRef))
            {
                return false;
            }
            if (isRef)
            {
                value = refValue;
                consumed = Amf3CommonValues.MARKER_LENGTH + headerConsumed;
            }

            var arrayConsumed = 0;
            var arrayBuffer = buffer.Slice(Amf3CommonValues.MARKER_LENGTH + headerConsumed);
            var itemCount = (int)headerData;

            if (!TryGetStringImpl(arrayBuffer, _readStringReferenceTable, out var key, out var keyConsumed))
            {
                return false;
            }
            arrayConsumed += keyConsumed;
            var array = new Dictionary<string, object>();
            if (key.Any())
            {
                do
                {
                    arrayBuffer = arrayBuffer.Slice(keyConsumed);
                    arrayConsumed += keyConsumed;
                    if (!TryGetValue(arrayBuffer, out var item, out var itemConsumed))
                    {
                        return false;
                    }

                    arrayConsumed += itemConsumed;
                    array.Add(key, item);
                    if (!TryGetStringImpl(arrayBuffer, _readStringReferenceTable, out key, out keyConsumed))
                    {
                        return false;
                    }
                }
                while (key.Any());
                if (array.Count != itemCount)
                {
                    return false;
                }
                value = array;
                consumed = Amf3CommonValues.MARKER_LENGTH + headerConsumed + arrayConsumed;
                return true;
            }

            for (int i = 0; i < itemCount; i++)
            {
                if (!TryGetValue(arrayBuffer, out var item, out var itemConsumed))
                {
                    return false;
                }
                array.Add(i.ToString(), item);
                arrayConsumed += itemConsumed;
            }

            value = array;
            consumed = Amf3CommonValues.MARKER_LENGTH + headerConsumed + arrayConsumed;
            _readObjectReferenceTable.Add(value);
            return true;
        }

        public bool TryGetObject(Span<byte> buffer, out object value, out int consumed)
        {
            value = default;
            consumed = default;
            if (!DataIsType(buffer, Amf3Type.Object))
            {
                return false;
            }

            if (!TryGetU29Impl(buffer.Slice(Amf3CommonValues.MARKER_LENGTH), out var header, out var headerLength))
            {
                return false;
            }

            if (!TryGetReference(header, _readObjectReferenceTable, out var headerData, out object refValue, out var isRef))
            {
                return false;
            }

            if (isRef)
            {
                value = refValue;
                consumed = Amf3CommonValues.MARKER_LENGTH + headerLength;
                return true;
            }
            Amf3ClassTraits traits = null;
            var traitsConsumed = 0;
            if ((header & 0x02) != 0x02)
            {
                var referenceIndex = (int)((header >> 2) & 0x3FFFFFFF);
                if (_readObjectTraitsReferenceTable.Count <= referenceIndex)
                {
                    return false;
                }

                if (_readObjectTraitsReferenceTable[referenceIndex] is Amf3ClassTraits obj)
                {
                    traits = obj;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                traits = new Amf3ClassTraits();
                var dataBuffer = buffer.Slice(Amf3CommonValues.MARKER_LENGTH + headerLength);
                if ((header & 0x04) == 0x04)
                {
                    traits.ClassType = Amf3ClassType.Externalizable;
                    if (!TryGetStringImpl(dataBuffer, _readStringReferenceTable, out var className, out int classNameConsumed))
                    {
                        return false;
                    }
                    traits.ClassName = className;
                    var externailzableBuffer = dataBuffer.Slice(classNameConsumed);

                    if (!_registeredExternalizable.TryGetValue(className, out var extType))
                    {
                        return false;
                    }
                    var extObj = Activator.CreateInstance(extType) as IExternalizable;
                    if (!extObj.TryDecodeData(externailzableBuffer, out var extConsumed))
                    {
                        return false;
                    }

                    value = extObj;
                    consumed = Amf3CommonValues.MARKER_LENGTH + headerLength + classNameConsumed + extConsumed;
                    return true;
                }


                if ((header & 0x08) != 0x08)
                {
                    traits.ClassType = Amf3ClassType.Dynamic;
                }
                else
                {
                    if (!TryGetStringImpl(dataBuffer, _readStringReferenceTable, out var className, out int classNameConsumed))
                    {
                        return false;
                    }
                    dataBuffer = dataBuffer.Slice(classNameConsumed);
                    traitsConsumed += classNameConsumed;
                    if (className.Any())
                    {
                        traits.ClassType = Amf3ClassType.Typed;
                        traits.ClassName = className;
                    }
                    else
                    {
                        traits.ClassType = Amf3ClassType.Anonymous;
                    }

                    if (!TryGetStringImpl(dataBuffer, _readStringReferenceTable, out var key, out int keyConsumed))
                    {
                        return false;
                    }
                    dataBuffer = dataBuffer.Slice(keyConsumed);
                    traitsConsumed += keyConsumed;
                    while (key.Any())
                    {
                        traits.Members.Add(key);
                        if (!TryGetStringImpl(dataBuffer, _readStringReferenceTable, out key, out keyConsumed))
                        {
                            return false;
                        }
                        dataBuffer = dataBuffer.Slice(keyConsumed);
                        traitsConsumed += keyConsumed;
                    }
                }
                _readObjectTraitsReferenceTable.Add(traits);
            }

            object deserailziedObject = null;
            var memberConsumed = 0;
            var valueBuffer = buffer.Slice(Amf3CommonValues.MARKER_LENGTH + headerLength + traitsConsumed);
            if (traits.ClassType == Amf3ClassType.Typed)
            {
                if (!_registeredTypedObeject.TryGetValue(traits.ClassName, out var classType))
                {
                    return false;
                }

                var props = classType.GetProperties();
                var propNames = props.Select(p => p.Name);
                if (!traits.Members.OrderBy(m => m).SequenceEqual(propNames.OrderBy(p => p)))
                {
                    return false;
                }

                deserailziedObject = Activator.CreateInstance(classType);
                foreach (var member in traits.Members)
                {
                    var prop = classType.GetProperty(member);
                    if (!TryGetValue(valueBuffer, out var data, out var dataConsumed))
                    {
                        return false;
                    }
                    valueBuffer = valueBuffer.Slice(dataConsumed);
                    memberConsumed += dataConsumed;
                    prop.SetValue(deserailziedObject, data);
                }
            }
            else
            {
                var obj = new Amf3Object();
                foreach (var member in traits.Members)
                {
                    if (!TryGetValue(valueBuffer, out var data, out var dataConsumed))
                    {
                        return false;
                    }
                    valueBuffer = valueBuffer.Slice(dataConsumed);
                    memberConsumed += dataConsumed;
                    obj.Add(member, data);
                }
                if (traits.ClassType == Amf3ClassType.Dynamic)
                {
                    if (!TryGetStringImpl(valueBuffer, _readStringReferenceTable, out var key, out var keyConsumed))
                    {
                        return false;
                    }
                    memberConsumed += keyConsumed;
                    valueBuffer = valueBuffer.Slice(keyConsumed);
                    while (key.Any())
                    {
                        if (!TryGetValue(valueBuffer, out var data, out var dataConsumed))
                        {
                            return false;
                        }
                        valueBuffer = valueBuffer.Slice(dataConsumed);
                        memberConsumed += dataConsumed;

                        obj.AddDynamic(key, data);

                        if (!TryGetStringImpl(valueBuffer, _readStringReferenceTable, out key, out keyConsumed))
                        {
                            return false;
                        }
                        valueBuffer = valueBuffer.Slice(keyConsumed);
                        memberConsumed += keyConsumed;
                    }
                }
            }

            value = deserailziedObject;
            consumed = Amf3CommonValues.MARKER_LENGTH + headerLength + traitsConsumed + memberConsumed;
            _readObjectReferenceTable.Add(value);
            return true;
        }

        public bool TryGetXml(Span<byte> buffer, out Amf3Xml value, out int consumed)
        {
            value = default;
            consumed = default;
            if (!DataIsType(buffer, Amf3Type.Xml))
            {
                return false;
            }

            var objectBuffer = buffer.Slice(Amf3CommonValues.MARKER_LENGTH);
            TryGetStringImpl(objectBuffer, _readObjectReferenceTable, out var str, out var strConsumed);
            var xml = new Amf3Xml();
            xml.LoadXml(str);

            value = xml;
            consumed = Amf3CommonValues.MARKER_LENGTH + strConsumed;
            return true;
        }

        public bool TryGetByteArray(Span<byte> buffer, out byte[] value, out int consumed)
        {
            value = default;
            consumed = default;
            if (!DataIsType(buffer, Amf3Type.ByteArray))
            {
                return false;
            }

            var objectBuffer = buffer.Slice(Amf3CommonValues.MARKER_LENGTH);

            if (!TryGetU29Impl(objectBuffer, out var header, out int headerLen))
            {
                return false;
            }

            if (!TryGetReference(header, _readObjectReferenceTable, out var headerData, out byte[] refValue, out var isRef))
            {
                return false;
            }
            if (isRef)
            {
                value = refValue;
                consumed = Amf3CommonValues.MARKER_LENGTH + headerLen;
            }

            var arrayLen = (int)headerData;
            if (objectBuffer.Length - headerLen < arrayLen)
            {
                return false;
            }

            value = new byte[arrayLen];

            objectBuffer.Slice(headerLen, arrayLen).CopyTo(value);
            _readObjectReferenceTable.Add(value);
            consumed = Amf3CommonValues.MARKER_LENGTH + headerLen + arrayLen;
            return true;
        }

        public bool TryGetValue(Span<byte> buffer, out object value, out int consumed)
        {
            value = default;
            consumed = default;

            if (!TryDescribeData(buffer, out var type))
            {
                return false;
            }

            if (!_readerHandlers.TryGetValue(type, out var handler))
            {
                return false;
            }

            if (!handler(buffer, out value, out consumed))
            {
                return false;
            }
            return true;
        }

        // Vector<Type> for typed vector otherwise Vector<object>
        public bool TryGetVectorObject(Span<byte> buffer, out object value, out int consumed)
        {
            value = default;
            consumed = default;
            if (!DataIsType(buffer, Amf3Type.VectorObject))
            {
                return false;
            }

            if (!TryGetVecotrObject(buffer.Slice(Amf3CommonValues.MARKER_LENGTH), out value, out consumed))
            {
                return false;
            }
            consumed += Amf3CommonValues.MARKER_LENGTH;
            return true;
        }

        public bool TryGetVectorInt(Span<byte> buffer, out Vector<int> value, out int consumed)
        {
            value = default;
            consumed = default;
            if (!DataIsType(buffer, Amf3Type.VectorUInt))
            {
                return false;
            }

            buffer = buffer.Slice(Amf3CommonValues.MARKER_LENGTH);
            if (!ReadVectorHeader(ref buffer, ref value, ref consumed, out var itemCount, out var isFixedSize, out var isRef))
            {
                return false;
            }

            if (isRef)
            {
                return true;
            }

            var vector = new Vector<int>();
            for (int i = 0; i < itemCount; i++)
            {
                if (!TryGetIntVectorData(ref buffer, vector, ref consumed))
                {
                    return false;
                }
            }

            value = vector;
            return true;
        }

        public bool TryGetVectorUInt(Span<byte> buffer, out Vector<uint> value, out int consumed)
        {
            value = default;
            consumed = default;
            if (!DataIsType(buffer, Amf3Type.VectorUInt))
            {
                return false;
            }

            buffer = buffer.Slice(Amf3CommonValues.MARKER_LENGTH);
            if (!ReadVectorHeader(ref buffer, ref value, ref consumed, out var itemCount, out var isFixedSize, out var isRef))
            {
                return false;
            }

            if (isRef)
            {
                return true;
            }

            var vector = new Vector<uint>();
            for (int i = 0; i < itemCount; i++)
            {
                if (!TryGetUIntVectorData(ref buffer, vector, ref consumed))
                {
                    return false;
                }
            }

            value = vector;
            return true;
        }

        public bool TryGetVectorDouble(Span<byte> buffer, out Vector<double> value, out int consumed)
        {
            value = default;
            consumed = default;
            if (!DataIsType(buffer, Amf3Type.VectorDouble))
            {
                return false;
            }

            buffer = buffer.Slice(Amf3CommonValues.MARKER_LENGTH);
            if (!ReadVectorHeader(ref buffer, ref value, ref consumed, out var itemCount, out var isFixedSize, out var isRef))
            {
                return false;
            }

            if (isRef)
            {
                return true;
            }

            var vector = new Vector<double>();
            for (int i = 0; i < itemCount; i++)
            {
                if (!TryGetDoubleVectorData(ref buffer, vector, ref consumed))
                {
                    return false;
                }
            }

            value = vector;
            return true;
        }

        private bool TryGetIntVectorData(ref Span<byte> buffer, Vector<int> vector, ref int consumed)
        {
            var value = RtmpBitConverter.ToInt32(buffer);
            vector.Add(value);
            consumed += sizeof(int);
            buffer = buffer.Slice(consumed);
            return true;

        }

        private bool TryGetUIntVectorData(ref Span<byte> buffer, Vector<uint> vector, ref int consumed)
        {
            var value = RtmpBitConverter.ToUInt32(buffer);
            vector.Add(value);
            consumed += sizeof(uint);
            buffer = buffer.Slice(consumed);
            return true;
        }

        private bool TryGetDoubleVectorData(ref Span<byte> buffer, Vector<double> vector, ref int consumed)
        {
            var value = RtmpBitConverter.ToDouble(buffer);
            vector.Add(value);
            consumed += sizeof(double);
            buffer = buffer.Slice(consumed);
            return true;

        }

        private bool TryGetReference<T, TTableEle>(uint header, List<TTableEle> referenceTable, out uint headerData, out T value, out bool isRef)
        {
            isRef = default;
            value = default;
            headerData = header >> 1;
            if ((header & 0x01) == 0x00)
            {
                var referenceIndex = (int)headerData;
                if (referenceTable.Count <= referenceIndex)
                {
                    return false;
                }
                if (referenceTable[referenceIndex] is T refObject)
                {
                    value = refObject;
                    isRef = true;
                    return true;
                }
                else
                {
                    return false;
                }
            }
            isRef = false;
            return true;
        }

        private bool ReadVectorHeader<T>(ref Span<byte> buffer, ref T value, ref int consumed, out int itemCount, out bool isFixedSize, out bool isRef)
        {
            isFixedSize = default;
            itemCount = default;
            isRef = default;
            if (!TryGetU29Impl(buffer, out var header, out var headerLength))
            {
                return false;
            }

            if (!TryGetReference(header, _readObjectReferenceTable, out var headerData, out T refValue, out isRef))
            {
                return false;
            }

            if (isRef)
            {
                value = refValue;
                consumed = Amf3CommonValues.MARKER_LENGTH + headerLength;
                return true;
            }

            itemCount = (int)headerData;

            var objectBuffer = buffer.Slice(headerLength);

            if (objectBuffer.Length < sizeof(byte))
            {
                return false;
            }

            isFixedSize = objectBuffer[0] == 0x01;
            buffer = objectBuffer.Slice(sizeof(byte));
            consumed = Amf3CommonValues.MARKER_LENGTH + headerLength;
            return true;
        }

        private bool ReadVectorTypeName(ref Span<byte> typeNameBuffer, out string typeName, out int typeNameConsumed)
        {
            typeName = default;
            typeNameConsumed = default;
            if (!TryGetStringImpl(typeNameBuffer, _readStringReferenceTable, out typeName, out typeNameConsumed))
            {
                return false;
            }
            typeNameBuffer = typeNameBuffer.Slice(typeNameConsumed);
            return true;
        }

        private bool TryGetVecotrObject(Span<byte> buffer, out object value, out int consumed)
        {
            value = default;
            consumed = default;

            int arrayConsumed = 0;

            if (!ReadVectorHeader(ref buffer, ref value, ref arrayConsumed, out var itemCount, out var isFixedSize, out var isRef))
            {
                return false;
            }

            if (isRef)
            {
                consumed = arrayConsumed;
                return true;
            }

            if (!ReadVectorTypeName(ref buffer, out var typeName, out var typeNameConsumed))
            {
                return false;
            }

            var arrayBodyBuffer = buffer;

            object resultVector = null;
            Type elementType = null;
            Action<object> addAction = null;
            if (typeName == "*")
            {
                elementType = typeof(object);
                var v = new Vector<object>();
                v.IsFixedSize = isFixedSize;
                resultVector = v;
                addAction = v.Add;
            }
            else
            {
                if (!_registeredTypedObeject.TryGetValue(typeName, out var type))
                {
                    return false;
                }
                elementType = type;

                var vectorType = typeof(Vector<>).MakeGenericType(type);
                resultVector = Activator.CreateInstance(vectorType);
                vectorType.GetProperty("IsFixedSize").SetValue(resultVector, isFixedSize);
                var addMethod = vectorType.GetMethod("Add");
                addAction = o => addMethod.Invoke(resultVector, new object[] { o });
            }
            for (int i = 0; i < itemCount; i++)
            {
                if (!TryGetValue(arrayBodyBuffer, out var item, out var itemConsumed))
                {
                    return false;
                }
                addAction(item);

                arrayBodyBuffer = arrayBodyBuffer.Slice(itemConsumed);
                arrayConsumed += itemConsumed;
            }
            _readObjectReferenceTable.Add(resultVector);
            value = resultVector;
            consumed = Amf3CommonValues.MARKER_LENGTH + /* fixed size */ sizeof(byte) + typeNameConsumed + arrayConsumed;
            return true;
        }

        public bool TryGetDictionary(Span<byte> buffer, out Amf3Dictionary<object, object> value, out int consumed)
        {
            value = default;
            consumed = default;
            if (!DataIsType(buffer, Amf3Type.Dictionary))
            {
                return false;
            }

            if (!TryGetU29Impl(buffer.Slice(Amf3CommonValues.MARKER_LENGTH), out var header, out var headerLength))
            {
                return false;
            }

            if (!TryGetReference(header, _readObjectReferenceTable, out var headerData, out Amf3Dictionary<object, object> refValue, out var isRef))
            {
                return false;
            }
            if (isRef)
            {
                value = refValue;
                consumed = Amf3CommonValues.MARKER_LENGTH + headerLength;
                return true;
            }

            var itemCount = (int)headerData;
            var dictConsumed = 0;
            if (buffer.Length - Amf3CommonValues.MARKER_LENGTH - headerLength < sizeof(byte))
            {
                return false;
            }
            var weakKeys = buffer[Amf3CommonValues.MARKER_LENGTH + headerLength] == 0x01;

            var dictBuffer = buffer.Slice(Amf3CommonValues.MARKER_LENGTH + headerLength + /* ignore weak key flag */ 1);
            var dict = new Amf3Dictionary<object, object>()
            {
                WeakKeys = weakKeys
            };
            for (int i = 0; i < itemCount; i++)
            {
                if (!TryGetValue(dictBuffer, out var key, out var keyConsumed))
                {
                    return false;
                }
                dictBuffer = dictBuffer.Slice(keyConsumed);
                dictConsumed += keyConsumed;
                if (!TryGetValue(dictBuffer, out var data, out var dataConsumed))
                {
                    return false;
                }
                dictBuffer = dictBuffer.Slice(dataConsumed);
                dict.Add(key, data);
                dictConsumed += dataConsumed;
            }
            _readObjectReferenceTable.Add(dict);
            value = dict;
            consumed = Amf3CommonValues.MARKER_LENGTH + headerLength + dictConsumed;
            return true;
        }
    }
}
