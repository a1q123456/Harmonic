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
        private delegate bool ReaderHandler<T>(Span<byte> buffer, out T value, out int consumed);
        private delegate bool ReaderHandler(Span<byte> buffer, out object value, out int consumed);
        
        private Dictionary<Amf3Type, ReaderHandler> _readerHandlers = new Dictionary<Amf3Type, ReaderHandler>();
        private Dictionary<string, Type> _registeredTypedObeject = new Dictionary<string, Type>();
        private Dictionary<string, IExternalizable> _registeredExternalizable = new Dictionary<string, IExternalizable>();

        
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

        public bool TryDescribeData(Span<byte> buffer, out Amf3Type type)
        {
            type = default;
            if (buffer.Length < MARKER_LENGTH)
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

        public bool TryGetUndefined(Span<byte> buffer, out Undefined value, out int consumed)
        {
            value = default;
            consumed = default;
            if (!TryDescribeData(buffer, out var type))
            {
                return false;
            }

            if (type != Amf3Type.Undefined)
            {
                return false;
            }
            value = new Undefined();
            consumed = MARKER_LENGTH;
            return true;
        }

        public bool TryGetNull(Span<byte> buffer, out object value, out int consumed)
        {
            value = default;
            consumed = default;
            if (!TryDescribeData(buffer, out var type))
            {
                return false;
            }

            if (type != Amf3Type.Null)
            {
                return false;
            }
            value = null;
            consumed = MARKER_LENGTH;
            return true;
        }

        public bool TryGetTrue(Span<byte> buffer, out bool value, out int consumed)
        {
            value = default;
            consumed = default;

            if (!TryDescribeData(buffer, out var type))
            {
                return false;
            }

            if (type != Amf3Type.True)
            {
                return false;
            }

            value = true;
            consumed = MARKER_LENGTH;
            return true;
        }

        public bool TryGetFalse(Span<byte> buffer, out bool value, out int consumed)
        {
            value = default;
            consumed = default;

            if (!TryDescribeData(buffer, out var type))
            {
                return false;
            }

            if (type != Amf3Type.False)
            {
                return false;
            }

            value = false;
            consumed = MARKER_LENGTH;
            return true;
        }

        public bool TryGetUInt29(Span<byte> buffer, out uint value, out int consumed)
        {
            value = default;
            consumed = default;

            if (!TryDescribeData(buffer, out var type))
            {
                return false;
            }
            if (type != Amf3Type.Integer)
            {
                return false;
            }
            
            
            var dataBuffer = buffer.Slice(MARKER_LENGTH);

            if (!TryGetU29Impl(dataBuffer, out value, out var dataConsumed))
            {
                return false;
            }

            consumed = MARKER_LENGTH + dataConsumed;

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

            if (!TryDescribeData(buffer, out var type))
            {
                return false;
            }

            if (type != Amf3Type.Double)
            {
                return false;
            }

            value = RtmpBitConverter.ToDouble(buffer.Slice(MARKER_LENGTH));
            consumed = MARKER_LENGTH + sizeof(double);
            return true;
        }

        public bool TryGetString(Span<byte> buffer, out string value, out int consumed)
        {
            value = default;
            consumed = default;

            if (!TryDescribeData(buffer, out var type))
            {
                return false;
            }

            if (type != Amf3Type.String)
            {
                return false;
            }

            var objectBuffer = buffer.Slice(MARKER_LENGTH);
            TryGetStringImpl(objectBuffer, _stringReferenceTable, out var str, out var strConsumed);
            value = str;
            consumed = MARKER_LENGTH + strConsumed;
            return true;
        }

        private bool TryGetStringImpl<T>(Span<byte> objectBuffer, List<T> referenceTable, out string value, out int consumed) where T: class
        {
            value = default;
            consumed = default;
            if (!TryGetU29Impl(objectBuffer, out var header, out int headerLen))
            {
                return false;
            }

            uint headerData = (header >> 1) & 0x7FFFFFFF;
            if ((header & 0x01) == 0x01)
            {
                var referenceIndex = (int)headerData;
                if (referenceTable.Count <= referenceIndex)
                {
                    return false;
                }
                if (referenceTable[referenceIndex] is string str)
                {
                    value = str;
                    consumed = MARKER_LENGTH + headerLen;
                    return true;
                }
                else
                {
                    return false;
                }
            }

            var strLen = (int)headerData;
            if (objectBuffer.Length - headerLen < strLen)
            {
                return false;
            }

            value = Encoding.UTF8.GetString(objectBuffer.Slice(headerLen, strLen));
            consumed = MARKER_LENGTH + headerLen + strLen;
            referenceTable.Add(value as T);
            return true;
        }

        public bool TryGetXmlDocument(Span<byte> buffer, out XmlDocument value, out int consumed)
        {
            value = default;
            consumed = default;

            if (!TryDescribeData(buffer, out var type))
            {
                return false;
            }

            if (type != Amf3Type.XmlDocument)
            {
                return false;
            }

            var objectBuffer = buffer.Slice(MARKER_LENGTH);
            TryGetStringImpl(objectBuffer, _objectReferenceTable, out var str, out var strConsumed);
            var xml = new XmlDocument();
            xml.LoadXml(str);
            value = xml;
            consumed = MARKER_LENGTH + strConsumed;
            return true;
        }

        public bool TryGetDate(Span<byte> buffer, out DateTime value, out int consumed)
        {
            value = default;
            consumed = default;

            if (!TryDescribeData(buffer, out var type))
            {
                return false;
            }

            if (type != Amf3Type.Date)
            {
                return false;
            }

            var objectBuffer = buffer.Slice(MARKER_LENGTH);

            if (!TryGetU29Impl(objectBuffer, out var header, out var headerLength))
            {
                return false;
            }
            var headerData = (header >> 1) & 0x7FFFFFFF;

            if ((header & 0x01) == 0x01)
            {
                var referenceIndex = (int)headerData;
                if (_objectReferenceTable.Count <= referenceIndex)
                {
                    return false;
                }
                if (_objectReferenceTable[referenceIndex] is DateTime dateTime)
                {
                    value = dateTime;
                    consumed = MARKER_LENGTH + headerLength;
                }
            }

            var timestamp = RtmpBitConverter.ToDouble(objectBuffer.Slice(headerLength));
            value = DateTimeOffset.FromUnixTimeMilliseconds((long)(timestamp * 1000)).DateTime;
            consumed = MARKER_LENGTH + headerLength + sizeof(double);
            _objectReferenceTable.Add(value);
            return true;
        }

        public bool TryGetArray(Span<byte> buffer, out Dictionary<string, object> value, out int consumed)
        {
            value = default;
            consumed = default;

            if (!TryDescribeData(buffer, out var type))
            {
                return false;
            }

            if (type != Amf3Type.Array)
            {
                return false;
            }

            if (!TryGetU29Impl(buffer.Slice(MARKER_LENGTH), out var header, out var headerConsumed))
            {
                return false;
            }

            var headerData = (header >> 1) & 0x7FFFFFFF;

            if ((header & 0x01) == 0x01)
            {
                var referenceIndex = (int)headerData;
                if (_objectReferenceTable.Count <= referenceIndex)
                {
                    return false;
                }

                if (_objectReferenceTable[referenceIndex] is Dictionary<string, object> refArray)
                {
                    value = refArray;
                    consumed = MARKER_LENGTH + headerConsumed;
                }
            }

            var arrayConsumed = 0;
            var arrayBuffer = buffer.Slice(MARKER_LENGTH + headerConsumed);
            var itemCount = (int)headerData;

            if (!TryGetStringImpl(arrayBuffer, _stringReferenceTable, out var key, out var keyConsumed))
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
                    if (!TryGetStringImpl(arrayBuffer, _stringReferenceTable, out key, out keyConsumed))
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
                consumed = MARKER_LENGTH + headerConsumed + arrayConsumed;
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
            consumed = MARKER_LENGTH + headerConsumed + arrayConsumed;
            _objectReferenceTable.Add(value);
            return true;
        }

        public bool TryGetObject(Span<byte> buffer, out object value, out int consumed)
        {
            value = default;
            consumed = default;

            if (!TryDescribeData(buffer, out var type))
            {
                return false;
            }

            if (type != Amf3Type.Object)
            {
                return false;
            }

            if (!TryGetU29Impl(buffer.Slice(MARKER_LENGTH), out var header, out var headerLength))
            {
                return false;
            }

            if ((header & 0x01) == 0x01)
            {
                var referenceIndex = (int)((header >> 1) & 0x7FFFFFFF);

                if (_objectReferenceTable.Count <= referenceIndex)
                {
                    return false;
                }

                if (_objectReferenceTable[referenceIndex] is object obj)
                {
                    value = obj;
                    consumed = MARKER_LENGTH + headerLength;
                    return true;
                }
                return false;
            }
            Amf3ClassTraits traits = null;
            var traitsConsumed = 0;
            if ((header & 0x02) != 0x02)
            {
                var referenceIndex = (int)((header >> 2) & 0x3FFFFFFF);
                if (_objectTraitsReferenceTable.Count <= referenceIndex)
                {
                    return false;
                }

                if (_objectTraitsReferenceTable[referenceIndex] is Amf3ClassTraits obj)
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
                var dataBuffer = buffer.Slice(MARKER_LENGTH + headerLength);
                if ((header & 0x04) == 0x04)
                {
                    traits.ClassType = Amf3ClassType.Externalizable;
                    if (!TryGetStringImpl(dataBuffer, _stringReferenceTable, out var className, out int classNameConsumed))
                    {
                        return false;
                    }
                    traits.ClassName = className;
                    var externailzableBuffer = dataBuffer.Slice(classNameConsumed);
                    
                    if (!_registeredExternalizable.TryGetValue(className, out var externalizable))
                    {
                        return false;
                    }
                    if (!externalizable.TryDecodeData(externailzableBuffer, out var extObj, out var extConsumed))
                    {
                        return false;
                    }

                    value = extObj;
                    consumed = MARKER_LENGTH + headerLength + classNameConsumed + extConsumed;
                    return true;
                }


                if ((header & 0x08) != 0x08)
                {
                    traits.ClassType = Amf3ClassType.Dynamic;
                }
                else
                {
                    if (!TryGetStringImpl(dataBuffer, _stringReferenceTable, out var className, out int classNameConsumed))
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

                    if (!TryGetStringImpl(dataBuffer, _stringReferenceTable, out var key, out int keyConsumed))
                    {
                        return false;
                    }
                    dataBuffer = dataBuffer.Slice(keyConsumed);
                    traitsConsumed += keyConsumed;
                    while (key.Any())
                    {
                        traits.Members.Add(key);
                        if (!TryGetStringImpl(dataBuffer, _stringReferenceTable, out key, out keyConsumed))
                        {
                            return false;
                        }
                        dataBuffer = dataBuffer.Slice(keyConsumed);
                        traitsConsumed += keyConsumed;
                    }
                }
                _objectTraitsReferenceTable.Add(traits);
            }

            object deserailziedObject = null;
            var memberConsumed = 0;
            var valueBuffer = buffer.Slice(MARKER_LENGTH + headerLength + traitsConsumed);
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
                dynamic dynamicObj = new ExpandoObject();
                var dictionary = (IDictionary<string, object>)dynamicObj;

                foreach (var member in traits.Members)
                {
                    if (!TryGetValue(valueBuffer, out var data, out var dataConsumed))
                    {
                        return false;
                    }
                    valueBuffer = valueBuffer.Slice(dataConsumed);
                    memberConsumed += dataConsumed;
                    dictionary.Add(member, data);
                }
                if (traits.ClassType == Amf3ClassType.Dynamic)
                {
                    if (!TryGetStringImpl(valueBuffer, _stringReferenceTable, out var key, out var keyConsumed))
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

                        dictionary.Add(key, data);

                        if (!TryGetStringImpl(valueBuffer, _stringReferenceTable, out key, out keyConsumed))
                        {
                            return false;
                        }
                        valueBuffer = valueBuffer.Slice(keyConsumed);
                        memberConsumed += keyConsumed;
                    }
                }
            }

            value = deserailziedObject;
            consumed = MARKER_LENGTH + headerLength + traitsConsumed + memberConsumed;
            _objectReferenceTable.Add(value);
            return true;
        }

        public bool TryGetXml(Span<byte> buffer, out XmlDocument value, out int consumed)
        {
            value = default;
            consumed = default;

            if (!TryDescribeData(buffer, out var type))
            {
                return false;
            }

            if (type != Amf3Type.Xml)
            {
                return false;
            }

            var objectBuffer = buffer.Slice(MARKER_LENGTH);
            TryGetStringImpl(objectBuffer, _objectReferenceTable, out var str, out var strConsumed);
            var xml = new XmlDocument();
            xml.LoadXml(str);
            
            value = xml;
            consumed = MARKER_LENGTH + strConsumed;
            return true;
        }

        public bool TryGetByteArray(Span<byte> buffer, out byte[] value, out int consumed)
        {
            value = default;
            consumed = default;
            if (!TryDescribeData(buffer, out var type))
            {
                return false;
            }

            if (type != Amf3Type.ByteArray)
            {
                return false;
            }

            var objectBuffer = buffer.Slice(MARKER_LENGTH);

            if (!TryGetU29Impl(objectBuffer, out var header, out int headerLen))
            {
                return false;
            }

            uint headerData = (header >> 1) & 0x7FFFFFFF;
            if ((header & 0x01) == 0x01)
            {
                var referenceIndex = (int)headerData;
                if (_objectReferenceTable.Count <= referenceIndex)
                {
                    return false;
                }
                if (_objectReferenceTable[referenceIndex] is byte[] byteArray)
                {
                    value = byteArray;
                    consumed = MARKER_LENGTH + headerLen;
                    return true;
                }
                else
                {
                    return false;
                }
            }

            var arrayLen = (int)headerData;
            if (objectBuffer.Length - headerLen < arrayLen)
            {
                return false;
            }

            value = new byte[arrayLen];

            objectBuffer.Slice(headerLen, arrayLen).CopyTo(value);
            _objectReferenceTable.Add(value);
            consumed = MARKER_LENGTH + headerLen + arrayLen;
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

        public bool TryGetVector<T>(Span<byte> buffer, out Vector<T> value, out int consumed)
        {
            value = default;
            consumed = default;

            if (!TryDescribeData(buffer, out var type))
            {
                return false;
            }

            if (type != Amf3Type.VectorInt)
            {
                return false;
            }

            if (!TryGetVecotrImpl(buffer.Slice(MARKER_LENGTH), out value, out consumed))
            {
                return false;
            }
            consumed += MARKER_LENGTH;
            return true;
        }

        private bool TryGetVecotrImpl<T>(Span<byte> buffer, out Vector<T> value, out int consumed)
        {
            value = default;
            consumed = default;

            if (!TryGetU29Impl(buffer, out var header, out var headerLength))
            {
                return false;
            }

            var headerData = (header >> 1) & 0x7FFFFFFFF;

            if ((header & 0x01) == 0x01)
            {
                var referenceIndex = (int)headerData;
                if (_objectReferenceTable.Count <= referenceIndex)
                {
                    return false;
                }
                if (_objectReferenceTable[referenceIndex] is Vector<T> refVector)
                {
                    value = refVector;
                    consumed = MARKER_LENGTH + headerLength;
                    return true;
                }
                return false;
            }

            var itemCount = (int)headerData;

            var objectBuffer = buffer.Slice(headerLength);

            if (objectBuffer.Length < sizeof(byte))
            {
                return false;
            }

            var isFixedSize = objectBuffer[0] == 0x01;
            var typeNameBuffer = objectBuffer.Slice(sizeof(byte));
            if (!TryGetStringImpl(typeNameBuffer, _stringReferenceTable, out var typeName, out var typeNameConsumed))
            {
                return false;
            }

            var resultVector = new Vector<T>();
            int arrayConsumed = 0;
            var arrayBodyBuffer = typeNameBuffer.Slice(typeNameConsumed);
            if (typeName != "*" && typeName != typeof(T).Name)
            {
                return false;
            }
            for (int i = 0; i < itemCount; i++)
            {
                if (!TryGetValue(arrayBodyBuffer, out var item, out var itemConsumed))
                {
                    return false;
                }
                if (item is T tItem)
                {
                    resultVector.Add(tItem);
                    arrayBodyBuffer = arrayBodyBuffer.Slice(itemConsumed);
                    arrayConsumed += itemConsumed;
                }
                else
                {
                    return false;
                }
            }
            _objectReferenceTable.Add(resultVector);
            value = resultVector;
            consumed = MARKER_LENGTH + headerLength + sizeof(byte) + typeNameConsumed + arrayConsumed;
            return true;
        }

        public bool TryGetDictionary(Span<byte> buffer, out Dictionary<object, object> value, out int consumed)
        {
            value = default;
            consumed = default;

            if (!TryDescribeData(buffer, out var type))
            {
                return false;
            }

            if (type != Amf3Type.Dictionary)
            {
                return false;
            }

            if (!TryGetU29Impl(buffer.Slice(MARKER_LENGTH), out var header, out var headerLength))
            {
                return false;
            }

            var headerData = (header >> 1) & 0x7FFFFFFF;

            if ((header & 0x01) == 0x01)
            {
                var referenceIndex = (int)(headerData);
                if (_objectReferenceTable.Count <= referenceIndex)
                {
                    return false;
                }
                if (_objectReferenceTable[referenceIndex] is Dictionary<object, object> refDict)
                {
                    value = refDict;
                    consumed = MARKER_LENGTH + headerLength;
                    return true;
                }
                return false;
            }

            var itemCount = (int)headerData;

            var dictConsumed = 0;
            var dictBuffer = buffer.Slice(MARKER_LENGTH + headerLength + /* ignore weak key flag */ 1);
            var dict = new Dictionary<object, object>();
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
            _objectReferenceTable.Add(dict);
            value = dict;
            consumed = MARKER_LENGTH + headerLength + dictConsumed;
            return true;
        }
    }
}
