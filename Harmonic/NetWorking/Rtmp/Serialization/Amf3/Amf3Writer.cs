using Harmonic.NetWorking.Rtmp.Common;
using Harmonic.NetWorking.Rtmp.Data;
using System;
using System.Linq;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.IO;
using Harmonic.Buffers;

namespace Harmonic.NetWorking.Rtmp.Serialization.Amf3
{
    public class Amf3Writer
    {
        private delegate bool WriteHandler<T>(T value);
        private delegate bool WriteHandler(object value);


        private List<string> _stringReferenceTable = new List<string>();
        private List<object> _objectReferenceTable = new List<object>();
        private List<object> _objectTraitsReferenceTable = new List<object>();
        private ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
        private UnlimitedBuffer _writerBuffer = new UnlimitedBuffer();

        private Dictionary<Type, WriteHandler> _writeHandlers = new Dictionary<Type, WriteHandler>();

        public int MessageLength => _writerBuffer.BufferLength;

        public Amf3Writer()
        {

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
            writeHandlers[typeof(Memory<byte>)] = WriteHandlerWrapper<Memory<byte>>(TryGetBytes);
            writeHandlers[typeof(string)] = WriteHandlerWrapper<string>(TryGetBytes);
            writeHandlers[typeof(Vector<int>)] = WriteHandlerWrapper<Vector<int>>(TryGetBytes);
            writeHandlers[typeof(Vector<uint>)] = WriteHandlerWrapper<Vector<uint>>(TryGetBytes);
            writeHandlers[typeof(Vector<double>)] = WriteHandlerWrapper<Vector<double>>(TryGetBytes);
            _writeHandlers = writeHandlers;
        }

        private WriteHandler WriteHandlerWrapper<T>(WriteHandler<T> handler)
        {
            return (object obj) =>
            {
                return handler((T)obj);
            };
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

        public void GetMessage(Span<byte> buffer)
        {
            _objectReferenceTable.Clear();
            _objectTraitsReferenceTable.Clear();
            _stringReferenceTable.Clear();
            _writerBuffer.TakeOutMemory(buffer);
        }

        public bool TryGetBytes(Undefined value)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.Undefined);
            return true;

        }

        public bool TryGetBytes(bool value)
        {
            if (value)
            {
                _writerBuffer.WriteToBuffer((byte)Amf3Type.True);
            }
            else
            {
                _writerBuffer.WriteToBuffer((byte)Amf3Type.False);
            }
            return true;

        }

        private bool TryGetU29BytesImpl(uint value)
        {
            var length = 0;
            if (value <= 0x7F)
            {
                length = 1;
            }
            else if (value <= 0x3FFF)
            {
                length = 2;
            }
            else if (value <= 0x1FFFFF)
            {
                length = 3;
            }
            else if (value <= 0x3FFFFFFF)
            {
                length = 4;
            }
            else
            {
                return false;
            }
            var arr = ArrayPool<byte>.Shared.Rent(4);
            try
            {
                RtmpBitConverter.TryGetBytes(value, arr);
                _writerBuffer.WriteToBuffer(arr[0]);
                for (int i = 1; i < length; i++)
                {
                    _writerBuffer.WriteToBuffer((byte)((arr[i] << i - 1) | (arr[i - 1] >> 9 - i)));
                }

            }
            finally
            {
                ArrayPool<byte>.Shared.Return(arr);
            }
            return true;
        }

        public bool TryGetU29Bytes(uint value)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.Integer);
            if (!TryGetU29BytesImpl(value))
            {
                return false;
            }
            return true;
        }

        public bool TryGetBytes(double value)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.Double);
            var backend = _arrayPool.Rent(sizeof(double));
            try
            {
                if (!RtmpBitConverter.TryGetBytes(value, backend))
                {
                    return false;
                }
                _writerBuffer.WriteToBuffer(backend.AsSpan(0, sizeof(double)));
            }
            finally
            {
                _arrayPool.Return(backend);
            }
            return true;

        }

        private bool TryGetStringBytesImpl<T>(string value, List<T> referenceTable)
        {
            if (value is T tValue)
            {
                var refIndex = referenceTable.IndexOf(tValue);
                if (refIndex >= 0)
                {
                    var header = (uint)refIndex << 1;
                    if (!TryGetU29BytesImpl(header))
                    {
                        return false;
                    }
                    return true;
                }
                else
                {
                    var byteCount = (uint)Encoding.UTF8.GetByteCount(value);
                    var header = (byteCount << 1) | 0x01;
                    if (!TryGetU29BytesImpl(header))
                    {
                        return false;
                    }
                    var backend = _arrayPool.Rent((int)byteCount);
                    try
                    {
                        Encoding.UTF8.GetBytes(value, backend);
                        _writerBuffer.WriteToBuffer(backend.AsSpan(0, (int)byteCount));
                    }
                    finally
                    {
                        _arrayPool.Return(backend);
                    }
                    
                    if (value.Any())
                    {
                        referenceTable.Add(tValue);
                    }
                    return true;
                }
            }
            else
            {
                return false;
            }
        }

        public bool TryGetBytes(string value)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.String);
            if (!TryGetStringBytesImpl(value, _stringReferenceTable))
            {
                return false;
            }

            return true;
        }

        public bool TryGetBytes(XmlDocument xml)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.XmlDocument);
            var content = XmlToString(xml);

            if (!TryGetStringBytesImpl(content, _objectReferenceTable))
            {
                return false;
            }
            
            return true;
        }

        public bool TryGetBytes(DateTime dateTime)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.Date);

            var refIndex = _objectReferenceTable.IndexOf(dateTime);
            uint header = 0;
            int headerConsumed = 0;
            if (refIndex >= 0)
            {
                header = (uint)refIndex << 1;

                if (!TryGetU29BytesImpl(header))
                {
                    return false;
                }
                
                return true;
            }

            var timeOffset = new DateTimeOffset(dateTime);
            var timestamp = timeOffset.ToUnixTimeMilliseconds() / 1000.0d;
            header = 0x01;
            if (!TryGetU29BytesImpl(header))
            {
                return false;
            }
            var backend = _arrayPool.Rent(sizeof(double));
            try
            {
                if (!RtmpBitConverter.TryGetBytes(timestamp, backend))
                {
                    return false;
                }
                _writerBuffer.WriteToBuffer(backend.AsSpan(0, sizeof(double)));
            }
            finally
            {
                _arrayPool.Return(backend);
            }

            _objectReferenceTable.Add(dateTime);
            return true;
        }

        public bool TryGetBytes(Amf3Xml xml)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.Xml);
            var content = XmlToString(xml);

            if (!TryGetStringBytesImpl(content, _objectReferenceTable))
            {
                return false;
            }

            return true;
        }

        public bool TryGetBytes(Memory<byte> value)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.ByteArray);
            uint header = 0;
            var refIndex = _objectReferenceTable.IndexOf(value);
            if (refIndex >= 0)
            {
                header = (uint)refIndex << 1;
                if (!TryGetU29BytesImpl(header))
                {
                    return false;
                }
                
                return true;
            }

            header = ((uint)value.Length << 1) | 0x01;
            if (!TryGetU29BytesImpl(header))
            {
                return false;
            }

            _writerBuffer.WriteToBuffer(value.Span);
            _objectReferenceTable.Add(value);
            return true;
        }

        public bool TryGetValueBytes(object value)
        {
            var valueType = value.GetType();

            if (!_writeHandlers.TryGetValue(valueType, out var handler))
            {
                return false;
            }

            if (!handler(value))
            {
                return false;
            }
            return true;
        }

        public bool TryGetBytes(object value)
        {
            uint header = 0;
            if (value == null)
            {
                _writerBuffer.WriteToBuffer((byte)Amf3Type.Null);
                return true;
            }
            else
            {
                _writerBuffer.WriteToBuffer((byte)Amf3Type.Object);
            }

            var refIndex = _objectReferenceTable.IndexOf(value);
            if (refIndex >= 0)
            {
                header = (uint)refIndex << 1;
                if (!TryGetU29BytesImpl(header))
                {
                    return false;
                }
                return true;
            }

            var objType = value.GetType();
            var traits = new Amf3ClassTraits();
            var memberValues = new List<object>();
            if (value is Amf3Object amf3Object)
            {
                if (amf3Object.IsAnonymous)
                {
                    traits.ClassName = "";
                    traits.ClassType = Amf3ClassType.Anonymous;
                }
                else
                {
                    traits.ClassName = "";
                    traits.ClassType = Amf3ClassType.Dynamic;
                }
                traits.Members = new List<string>(amf3Object.Fieldes.Keys);
                memberValues = new List<object>(amf3Object.Fieldes.Keys.Select(k => amf3Object.Fieldes[k]));
            }
            else if (value is IExternalizable)
            {
                traits.ClassName = objType.Name;
                traits.ClassType = Amf3ClassType.Externalizable;
            }
            else
            {
                traits.ClassName = objType.Name;
                traits.ClassType = Amf3ClassType.Typed;
                traits.Members = new List<string>(objType.GetProperties().Select(p => p.Name));
                memberValues = new List<object>(objType.GetProperties().Select(p => p.GetValue(value)));
            }

            var traitRefIndex = _objectTraitsReferenceTable.IndexOf(traits);
            if (traitRefIndex >= 0)
            {
                header = ((uint)traitRefIndex << 2) | 0x03;
                if (!TryGetU29BytesImpl(header))
                {
                    return false;
                }
            }
            else
            {
                if (traits.ClassType == Amf3ClassType.Externalizable)
                {
                    header = 0x07;
                    if (!TryGetU29BytesImpl(header))
                    {
                        return false;
                    }
                    if (!TryGetStringBytesImpl(traits.ClassName, _stringReferenceTable))
                    {
                        return false;
                    }
                    var extObj = value as IExternalizable;
                    if (!extObj.TryEncodeData(_writerBuffer))
                    {
                        return false;
                    }
                    return true;
                }
                else
                {
                    header = 0x03;
                    if (traits.ClassType == Amf3ClassType.Dynamic)
                    {
                        header |= 0x08;
                    }
                    var memberCount = (uint)traits.Members.Count;
                    header |= memberCount << 4;
                    if (!TryGetU29BytesImpl(header))
                    {
                        return false;
                    }

                    foreach (var memberName in traits.Members)
                    {
                        if (!TryGetStringBytesImpl(memberName, _stringReferenceTable))
                        {
                            return false;
                        }
                    }
                }
                _objectTraitsReferenceTable.Add(traits);
            }

            foreach (var memberValue in memberValues)
            {
                if (!TryGetValueBytes(memberValue))
                {
                    return false;
                }
            }

            if (traits.ClassType == Amf3ClassType.Dynamic)
            {
                var amf3Obj = value as Amf3Object;
                foreach ((var key, var item) in amf3Obj.DynamicFields)
                {
                    if (!TryGetStringBytesImpl(key, _stringReferenceTable))
                    {
                        return false;
                    }
                    if (!TryGetValueBytes(item))
                    {
                        return false;
                    }
                }
            }
            _objectReferenceTable.Add(value);
            return true;
        }

        public bool TryGetBytes(Vector<uint> value, Span<byte> buffer, out int consumed)
        {
            consumed = 0;
            if (buffer.Length < Amf3CommonValues.MARKER_LENGTH)
            {
                return false;
            }
            buffer[0] = (byte)Amf3Type.VectorDouble;
            buffer = buffer.Slice(Amf3CommonValues.MARKER_LENGTH);
            consumed += Amf3CommonValues.MARKER_LENGTH;

            var refIndex = _objectReferenceTable.IndexOf(value);
            if (refIndex >= 0)
            {
                var header = (uint)refIndex << 1;
                if (!TryGetU29BytesImpl(header))
                {
                    return false;
                }
                return true;
            }
            else
            {
                var header = ((uint)value.Count << 1) | 0x01;
                if (!TryGetU29BytesImpl(header))
                {
                    return false;
                }
                if (!RtmpBitConverter.TryGetBytes(value.IsFixedSize ? 0x01 : 0x00, buffer))
                {
                    return false;
                }
                buffer = buffer.Slice(sizeof(byte));
                consumed += sizeof(byte);
                foreach (var i in value)
                {
                    if (!RtmpBitConverter.TryGetBytes(i, buffer))
                    {
                        return false;
                    }
                    buffer = buffer.Slice(sizeof(uint));
                    consumed += sizeof(uint);
                }
                _objectReferenceTable.Add(value);
                return true;
            }
        }

        public bool TryGetBytes(Vector<int> value, Span<byte> buffer, out int consumed)
        {
            consumed = 0;
            if (buffer.Length < Amf3CommonValues.MARKER_LENGTH)
            {
                return false;
            }
            buffer[0] = (byte)Amf3Type.VectorDouble;
            buffer = buffer.Slice(Amf3CommonValues.MARKER_LENGTH);
            consumed += Amf3CommonValues.MARKER_LENGTH;

            var refIndex = _objectReferenceTable.IndexOf(value);
            if (refIndex >= 0)
            {
                var header = (uint)refIndex << 1;
                if (!TryGetU29BytesImpl(header))
                {
                    return false;
                }
                return true;
            }
            else
            {
                var header = ((uint)value.Count << 1) | 0x01;
                if (!TryGetU29BytesImpl(header))
                {
                    return false;
                }
                if (!RtmpBitConverter.TryGetBytes(value.IsFixedSize ? 0x01 : 0x00, buffer))
                {
                    return false;
                }
                buffer = buffer.Slice(sizeof(byte));
                consumed += sizeof(byte);
                foreach (var i in value)
                {
                    if (!RtmpBitConverter.TryGetBytes(i, buffer))
                    {
                        return false;
                    }
                    buffer = buffer.Slice(sizeof(int));
                    consumed += sizeof(int);
                }
                _objectReferenceTable.Add(value);
                return true;
            }
        }

        public bool TryGetBytes(Vector<double> value, Span<byte> buffer, out int consumed)
        {
            consumed = 0;
            if (buffer.Length < Amf3CommonValues.MARKER_LENGTH)
            {
                return false;
            }
            buffer[0] = (byte)Amf3Type.VectorDouble;
            buffer = buffer.Slice(Amf3CommonValues.MARKER_LENGTH);
            consumed += Amf3CommonValues.MARKER_LENGTH;

            var refIndex = _objectReferenceTable.IndexOf(value);
            if (refIndex >= 0)
            {
                var header = (uint)refIndex << 1;
                if (!TryGetU29BytesImpl(header))
                {
                    return false;
                }
                return true;
            }
            else
            {
                var header = ((uint)value.Count << 1) | 0x01;
                if (!TryGetU29BytesImpl(header))
                {
                    return false;
                }
                if (!RtmpBitConverter.TryGetBytes(value.IsFixedSize ? 0x01 : 0x00, buffer))
                {
                    return false;
                }
                buffer = buffer.Slice(sizeof(byte));
                consumed += sizeof(byte);
                foreach (var i in value)
                {
                    if (!RtmpBitConverter.TryGetBytes(i, buffer))
                    {
                        return false;
                    }
                    buffer = buffer.Slice(sizeof(double));
                    consumed += sizeof(double);
                }
                _objectReferenceTable.Add(value);
                return true;
            }
        }

        public bool TryGetBytes<T>(Vector<T> value)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.VectorDouble);

            var refIndex = _objectReferenceTable.IndexOf(value);
            if (refIndex >= 0)
            {
                var header = (uint)refIndex << 1;
                if (!TryGetU29BytesImpl(header))
                {
                    return false;
                }
                return true;
            }
            else
            {
                var header = ((uint)value.Count << 1) | 0x01;
                if (!TryGetU29BytesImpl(header))
                {
                    return false;
                }
                _writerBuffer.WriteToBuffer(value.IsFixedSize ? (byte)0x01 : (byte)0x00);

                var className = typeof(T) == typeof(object) ? "*" : typeof(T).Name;
                if (!TryGetStringBytesImpl(className, _stringReferenceTable))
                {
                    return false;
                }

                foreach (var i in value)
                {
                    if (!TryGetValueBytes((object)i))
                    {
                        return false;
                    }
                }
                _objectReferenceTable.Add(value);
                return true;
            }

        }

        public bool TryGetBytes(Dictionary<string, object> value)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.Array);

            var refIndex = _objectReferenceTable.IndexOf(value);
            if (refIndex >= 0)
            {
                var header = (uint)refIndex << 1;
                if (!TryGetU29BytesImpl(header))
                {
                    return false;
                }
                return true;
            }
            else
            {
                var header = ((uint)value.Count << 1) & 0x01;
                if (!TryGetU29BytesImpl(header))
                {
                    return false;
                }
                var isDense = false;

                try
                {
                    var ind = value.Keys.Select(s => int.Parse(s)).ToArray();
                    for (int i = 1; i < ind.Length; i++)
                    {
                        if (ind[i - 1] - ind[i] != 1)
                        {
                            isDense = false;
                        }
                    }

                }
                catch (Exception e) when (e is FormatException || e is OverflowException)
                {
                    isDense = false;
                }

                if (!isDense)
                {
                    if (!TryGetStringBytesImpl("", _stringReferenceTable))
                    {
                        return false;
                    }
                    foreach (var i in value.Keys)
                    {
                        if (!TryGetValueBytes(value[i]))
                        {
                            return false;
                        }
                    }
                }
                else
                {
                    foreach ((var key, var item) in value)
                    {
                        if (!TryGetStringBytesImpl(key, _stringReferenceTable))
                        {
                            return false;
                        }

                        if (!TryGetValueBytes(item))
                        {
                            return false;
                        }
                    }
                }
                _objectReferenceTable.Add(value);
                return true;
            }
        }

        public bool TryGetBytes<TKey, TValue>(Amf3Dictionary<TKey, TValue> value)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.Dictionary);

            var refIndex = _objectReferenceTable.IndexOf(value);

            if (refIndex >= 0)
            {
                var header = (uint)refIndex << 1;
                if (!TryGetU29BytesImpl(header))
                {
                    return false;
                };
                return true;
            }
            else
            {
                var header = (uint)value.Count << 1;
                if (!TryGetU29BytesImpl(header))
                {
                    return false;
                }
                _writerBuffer.WriteToBuffer((byte)(value.WeakKeys ? 0x01 : 0x00));
                foreach ((var key, var item) in value)
                {
                    if (!TryGetValueBytes(key))
                    {
                        return false;
                    }
                    if (!TryGetValueBytes(item))
                    {
                        return false;
                    }
                }
                _objectReferenceTable.Add(value);
                return true;
            }
        }

    }
}
