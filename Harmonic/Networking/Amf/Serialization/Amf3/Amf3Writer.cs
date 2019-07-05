using Harmonic.Networking.Amf.Common;
using System;
using System.Linq;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.IO;
using Harmonic.Buffers;
using Harmonic.Networking.Utils;
using Harmonic.Networking.Amf.Data;
using System.Diagnostics.Contracts;
using System.Reflection;
using Harmonic.Networking.Amf.Attributes;

namespace Harmonic.Networking.Amf.Serialization.Amf3
{
    public class Amf3Writer
    {
        private delegate void WriteHandler<T>(T value);
        private delegate void WriteHandler(object value);


        private List<string> _stringReferenceTable = new List<string>();
        private List<object> _objectReferenceTable = new List<object>();
        private List<object> _objectTraitsReferenceTable = new List<object>();
        private ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
        private UnlimitedBuffer _writerBuffer = new UnlimitedBuffer();

        private Dictionary<Type, WriteHandler> _writeHandlers = new Dictionary<Type, WriteHandler>();

        public int MessageLength => _writerBuffer.BufferLength;
        public static readonly uint U29MAX = 0x1FFFFFFF;
        private MethodInfo _writeVectorTMethod = null;
        private MethodInfo _writeDictionaryTMethod = null;

        public Amf3Writer()
        {

            var writeHandlers = new Dictionary<Type, WriteHandler>();
            writeHandlers[typeof(int)] = WriteHandlerWrapper<double>(WriteBytes);
            writeHandlers[typeof(uint)] = WriteHandlerWrapper<uint>(WriteBytes);
            writeHandlers[typeof(long)] = WriteHandlerWrapper<double>(WriteBytes);
            writeHandlers[typeof(ulong)] = WriteHandlerWrapper<uint>(WriteBytes);
            writeHandlers[typeof(short)] = WriteHandlerWrapper<double>(WriteBytes);
            writeHandlers[typeof(ushort)] = WriteHandlerWrapper<uint>(WriteBytes);
            writeHandlers[typeof(double)] = WriteHandlerWrapper<double>(WriteBytes);
            writeHandlers[typeof(Undefined)] = WriteHandlerWrapper<Undefined>(WriteBytes);
            writeHandlers[typeof(object)] = WriteHandlerWrapper<object>(WriteBytes);
            writeHandlers[typeof(DateTime)] = WriteHandlerWrapper<DateTime>(WriteBytes);
            writeHandlers[typeof(XmlDocument)] = WriteHandlerWrapper<XmlDocument>(WriteBytes);
            writeHandlers[typeof(Amf3Xml)] = WriteHandlerWrapper<Amf3Xml>(WriteBytes);
            writeHandlers[typeof(bool)] = WriteHandlerWrapper<bool>(WriteBytes);
            writeHandlers[typeof(Memory<byte>)] = WriteHandlerWrapper<Memory<byte>>(WriteBytes);
            writeHandlers[typeof(string)] = WriteHandlerWrapper<string>(WriteBytes);
            writeHandlers[typeof(Vector<int>)] = WriteHandlerWrapper<Vector<int>>(WriteBytes);
            writeHandlers[typeof(Vector<uint>)] = WriteHandlerWrapper<Vector<uint>>(WriteBytes);
            writeHandlers[typeof(Vector<double>)] = WriteHandlerWrapper<Vector<double>>(WriteBytes);
            writeHandlers[typeof(Vector<>)] = WrapVector;
            writeHandlers[typeof(Amf3Dictionary<,>)] = WrapDictionary;
            _writeHandlers = writeHandlers;

            Action<Vector<int>> method = WriteBytes<int>;
            _writeVectorTMethod = method.Method.GetGenericMethodDefinition();

            Action<Amf3Dictionary<int, int>> dictMethod = WriteBytes;
            _writeDictionaryTMethod = dictMethod.Method.GetGenericMethodDefinition();

        }

        private void WrapVector(object value)
        {
            var valueType = value.GetType();
            Contract.Assert(valueType.IsGenericType);
            var defination = valueType.GetGenericTypeDefinition();
            Contract.Assert(defination == typeof(Vector<>));
            var vectorT = valueType.GetGenericArguments().First();

            _writeVectorTMethod.MakeGenericMethod(vectorT).Invoke(this, new object[] { value });
        }

        private void WrapDictionary(object value)
        {
            var valueType = value.GetType();
            Contract.Assert(valueType.IsGenericType);
            var defination = valueType.GetGenericTypeDefinition();
            Contract.Assert(defination == typeof(Amf3Dictionary<,>));
            var tKey = valueType.GetGenericArguments().First();
            var tValue = valueType.GetGenericArguments().Last();

            _writeDictionaryTMethod.MakeGenericMethod(tKey, tValue).Invoke(this, new object[] { value });
        }

        private WriteHandler WriteHandlerWrapper<T>(WriteHandler<T> handler)
        {
            return (object obj) =>
            {
                if (obj is T tObj)
                {
                    handler(tObj);
                }
                else
                {
                    handler((T)Convert.ChangeType(obj, typeof(T)));
                }

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

        public void WriteBytes(Undefined value)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.Undefined);
        }

        public void WriteBytes(bool value)
        {
            if (value)
            {
                _writerBuffer.WriteToBuffer((byte)Amf3Type.True);
            }
            else
            {
                _writerBuffer.WriteToBuffer((byte)Amf3Type.False);
            }

        }

        private void WriteU29BytesImpl(uint value)
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
            else if (value <= U29MAX)
            {
                length = 4;
            }
            else
            {
                throw new ArgumentOutOfRangeException();
            }
            var arr = ArrayPool<byte>.Shared.Rent(4);
            try
            {
                NetworkBitConverter.TryGetBytes(value, arr);

                switch (length)
                {
                    case 4:
                        _writerBuffer.WriteToBuffer((byte)(arr[0] << 2 | ((arr[1]) >> 6) | 0x80));
                        _writerBuffer.WriteToBuffer((byte)(arr[1] << 1 | ((arr[2]) >> 7) | 0x80));
                        _writerBuffer.WriteToBuffer((byte)(arr[2] | 0x80));
                        _writerBuffer.WriteToBuffer(arr[3]);
                        break;
                    case 3:
                        _writerBuffer.WriteToBuffer((byte)(arr[1] << 2 | ((arr[2]) >> 6) | 0x80));
                        _writerBuffer.WriteToBuffer((byte)(arr[2] << 1 | ((arr[3]) >> 7) | 0x80));
                        _writerBuffer.WriteToBuffer((byte)(arr[3] & 0x7F));
                        break;
                    case 2:
                        _writerBuffer.WriteToBuffer((byte)(arr[2] << 1 | ((arr[3]) >> 7) | 0x80));
                        _writerBuffer.WriteToBuffer((byte)(arr[3] & 0x7F));
                        break;
                    case 1:
                        _writerBuffer.WriteToBuffer((byte)(arr[3]));
                        break;
                    default:
                        throw new ApplicationException();

                }

            }
            finally
            {
                ArrayPool<byte>.Shared.Return(arr);
            }
        }

        public void WriteBytes(uint value)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.Integer);
            WriteU29BytesImpl(value);
        }

        public void WriteBytes(double value)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.Double);
            var backend = _arrayPool.Rent(sizeof(double));
            try
            {
                Contract.Assert(NetworkBitConverter.TryGetBytes(value, backend));
                _writerBuffer.WriteToBuffer(backend.AsSpan(0, sizeof(double)));
            }
            finally
            {
                _arrayPool.Return(backend);
            }

        }

        private void WriteStringBytesImpl<T>(string value, List<T> referenceTable)
        {
            if (value is T tValue)
            {
                var refIndex = referenceTable.IndexOf(tValue);
                if (refIndex >= 0)
                {
                    var header = (uint)refIndex << 1;
                    WriteU29BytesImpl(header);
                    return;
                }
                else
                {
                    var byteCount = (uint)Encoding.UTF8.GetByteCount(value);
                    var header = (byteCount << 1) | 0x01;
                    WriteU29BytesImpl(header);
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
                }
            }
            else
            {
                Contract.Assert(false);
            }
        }

        public void WriteBytes(string value)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.String);
            WriteStringBytesImpl(value, _stringReferenceTable);
        }

        public void WriteBytes(XmlDocument xml)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.XmlDocument);
            var content = XmlToString(xml);

            WriteStringBytesImpl(content, _objectReferenceTable);
        }

        public void WriteBytes(DateTime dateTime)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.Date);

            var refIndex = _objectReferenceTable.IndexOf(dateTime);
            uint header = 0;
            if (refIndex >= 0)
            {
                header = (uint)refIndex << 1;

                WriteU29BytesImpl(header);
                return;
            }
            _objectReferenceTable.Add(dateTime);

            var timeOffset = new DateTimeOffset(dateTime);
            var timestamp = timeOffset.ToUnixTimeMilliseconds();
            header = 0x01;
            WriteU29BytesImpl(header);
            var backend = _arrayPool.Rent(sizeof(double));
            try
            {
                Contract.Assert(NetworkBitConverter.TryGetBytes(timestamp, backend));
                _writerBuffer.WriteToBuffer(backend.AsSpan(0, sizeof(double)));
            }
            finally
            {
                _arrayPool.Return(backend);
            }

        }

        public void WriteBytes(Amf3Xml xml)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.Xml);
            var content = XmlToString(xml);

            WriteStringBytesImpl(content, _objectReferenceTable);
        }

        public void WriteBytes(Memory<byte> value)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.ByteArray);
            uint header = 0;
            var refIndex = _objectReferenceTable.IndexOf(value);
            if (refIndex >= 0)
            {
                header = (uint)refIndex << 1;
                WriteU29BytesImpl(header);
                return;
            }

            header = ((uint)value.Length << 1) | 0x01;
            WriteU29BytesImpl(header);

            _writerBuffer.WriteToBuffer(value.Span);
            _objectReferenceTable.Add(value);
        }

        public void WriteValueBytes(object value)
        {
            if (value == null)
            {
                WriteBytes((object)null);
                return;
            }
            var valueType = value.GetType();
            if (_writeHandlers.TryGetValue(valueType, out var handler))
            {
                handler(value);
            }
            else
            {
                if (valueType.IsGenericType)
                {
                    var genericDefination = valueType.GetGenericTypeDefinition();

                    if (genericDefination != typeof(Vector<>) && genericDefination != typeof(Amf3Dictionary<,>))
                    {
                        throw new NotSupportedException();
                    }
                    
                    if (_writeHandlers.TryGetValue(genericDefination, out handler))
                    {
                        handler(value);
                    }
                }
                else if (typeof(IDynamicObject).IsAssignableFrom(valueType))
                {
                    WriteBytes(value);
                }
                else
                {
                    Contract.Assert(false);
                }
            }

            
        }

        public void WriteBytes(object value)
        {
            uint header = 0;
            if (value == null)
            {
                _writerBuffer.WriteToBuffer((byte)Amf3Type.Null);
                return;
            }
            else
            {
                _writerBuffer.WriteToBuffer((byte)Amf3Type.Object);
            }

            var refIndex = _objectReferenceTable.IndexOf(value);
            if (refIndex >= 0)
            {
                header = (uint)refIndex << 1;
                WriteU29BytesImpl(header);
                return;
            }

            var objType = value.GetType();
            string attrTypeName = null;
            var classAttr = objType.GetCustomAttribute<TypedObjectAttribute>();
            if (classAttr != null)
            {
                attrTypeName = classAttr.Name;
            }
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
                    traits.ClassName = attrTypeName ?? objType.Name;
                    traits.ClassType = Amf3ClassType.Typed;
                }
                traits.IsDynamic = amf3Object.IsDynamic;
                traits.Members = new List<string>(amf3Object.Fields.Keys);
                memberValues = new List<object>(amf3Object.Fields.Keys.Select(k => amf3Object.Fields[k]));
            }
            else if (value is IExternalizable)
            {
                traits.ClassName = attrTypeName ?? objType.Name;
                traits.ClassType = Amf3ClassType.Externalizable;
            }
            else
            {
                traits.ClassName = attrTypeName ?? objType.Name;
                traits.ClassType = Amf3ClassType.Typed;
                var props = objType.GetProperties();
                foreach (var prop in props)
                {
                    var attr = (ClassFieldAttribute)Attribute.GetCustomAttribute(prop, typeof(ClassFieldAttribute));
                    if (attr != null)
                    {
                        traits.Members.Add(attr.Name ?? prop.Name);
                        memberValues.Add(prop.GetValue(value));
                    }
                }
                traits.IsDynamic = value is IDynamicObject;
            }
            _objectReferenceTable.Add(value);


            var traitRefIndex = _objectTraitsReferenceTable.IndexOf(traits);
            if (traitRefIndex >= 0)
            {
                header = ((uint)traitRefIndex << 2) | 0x03;
                WriteU29BytesImpl(header);
                return;
            }
            else
            {
                if (traits.ClassType == Amf3ClassType.Externalizable)
                {
                    header = 0x07;
                    WriteU29BytesImpl(header);
                    WriteStringBytesImpl(traits.ClassName, _stringReferenceTable);
                    var extObj = value as IExternalizable;
                    extObj.TryEncodeData(_writerBuffer);
                    return;
                }
                else
                {
                    header = 0x03;
                    if (traits.IsDynamic)
                    {
                        header |= 0x08;
                    }
                    var memberCount = (uint)traits.Members.Count;
                    header |= memberCount << 4;
                    WriteU29BytesImpl(header);
                    WriteStringBytesImpl(traits.ClassName, _stringReferenceTable);

                    foreach (var memberName in traits.Members)
                    {
                        WriteStringBytesImpl(memberName, _stringReferenceTable);
                    }
                }
                _objectTraitsReferenceTable.Add(traits);
            }


            foreach (var memberValue in memberValues)
            {
                WriteValueBytes(memberValue);
            }

            if (traits.IsDynamic)
            {
                var amf3Obj = value as IDynamicObject;
                foreach ((var key, var item) in amf3Obj.DynamicFields)
                {
                    WriteStringBytesImpl(key, _stringReferenceTable);
                    WriteValueBytes(item);
                }
                WriteStringBytesImpl("", _stringReferenceTable);
            }
        }

        public void WriteBytes(Vector<uint> value)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.VectorUInt);

            var refIndex = _objectReferenceTable.IndexOf(value);
            if (refIndex >= 0)
            {
                var header = (uint)refIndex << 1;
                WriteU29BytesImpl(header);
                return;
            }
            else
            {
                _objectReferenceTable.Add(value);
                var header = ((uint)value.Count << 1) | 0x01;
                WriteU29BytesImpl(header);
                _writerBuffer.WriteToBuffer(value.IsFixedSize ? (byte)0x01 : (byte)0x00);
                var buffer = _arrayPool.Rent(sizeof(uint));
                try
                {
                    foreach (var i in value)
                    {
                        Contract.Assert(NetworkBitConverter.TryGetBytes(i, buffer));
                        _writerBuffer.WriteToBuffer(buffer.AsSpan(0, sizeof(uint)));
                    }
                }
                finally
                {
                    _arrayPool.Return(buffer);
                }
            }
        }

        public void WriteBytes(Vector<int> value)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.VectorInt);

            var refIndex = _objectReferenceTable.IndexOf(value);
            if (refIndex >= 0)
            {
                var header = (uint)refIndex << 1;
                WriteU29BytesImpl(header);
                return;
            }
            else
            {
                _objectReferenceTable.Add(value);
                var header = ((uint)value.Count << 1) | 0x01;
                WriteU29BytesImpl(header);
                _writerBuffer.WriteToBuffer(value.IsFixedSize ? (byte)0x01 : (byte)0x00);
                var buffer = _arrayPool.Rent(sizeof(int));
                try
                {

                    foreach (var i in value)
                    {
                        Contract.Assert(NetworkBitConverter.TryGetBytes(i, buffer));
                        _writerBuffer.WriteToBuffer(buffer.AsSpan(0, sizeof(int)));
                    }
                }
                finally
                {
                    _arrayPool.Return(buffer);
                }
                return;
            }
        }

        public void WriteBytes(Vector<double> value)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.VectorDouble);

            var refIndex = _objectReferenceTable.IndexOf(value);
            if (refIndex >= 0)
            {
                var header = (uint)refIndex << 1;
                WriteU29BytesImpl(header);
                return;
            }
            else
            {
                _objectReferenceTable.Add(value);
                var header = ((uint)value.Count << 1) | 0x01;
                WriteU29BytesImpl(header);
                _writerBuffer.WriteToBuffer(value.IsFixedSize ? (byte)0x01 : (byte)0x00);
                var buffer = _arrayPool.Rent(sizeof(double));
                try
                {
                    foreach (var i in value)
                    {
                        Contract.Assert(NetworkBitConverter.TryGetBytes(i, buffer));
                        _writerBuffer.WriteToBuffer(buffer.AsSpan(0, sizeof(double)));
                    }
                }
                finally
                {
                    _arrayPool.Return(buffer);
                }
                return;
            }
        }

        public void WriteBytes<T>(Vector<T> value)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.VectorObject);

            var refIndex = _objectReferenceTable.IndexOf(value);
            if (refIndex >= 0)
            {
                var header = (uint)refIndex << 1;
                WriteU29BytesImpl(header);
                return;
            }
            else
            {
                _objectReferenceTable.Add(value);
                var header = ((uint)value.Count << 1) | 0x01;
                WriteU29BytesImpl(header);
                _writerBuffer.WriteToBuffer(value.IsFixedSize ? (byte)0x01 : (byte)0x00);
                var tType = typeof(T);

                string typeName = tType.Name;
                var attr = tType.GetCustomAttribute<TypedObjectAttribute>();
                if (attr != null)
                {
                    typeName = attr.Name;
                }

                var className = typeof(T) == typeof(object) ? "*" : typeName;
                WriteStringBytesImpl(className, _stringReferenceTable);

                foreach (var i in value)
                {
                    WriteValueBytes(i);
                }
                return;
            }

        }

        public void WriteBytes(Amf3Array value)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.Array);

            var refIndex = _objectReferenceTable.IndexOf(value);
            if (refIndex >= 0)
            {
                var header = (uint)refIndex << 1;
                WriteU29BytesImpl(header);
                return;
            }
            else
            {
                _objectReferenceTable.Add(value);
                var header = ((uint)value.DensePart.Count << 1) | 0x01;
                WriteU29BytesImpl(header);
                foreach ((var key, var item) in value.SparsePart)
                {
                    WriteStringBytesImpl(key, _stringReferenceTable);
                    WriteValueBytes(item);
                }
                WriteStringBytesImpl("", _stringReferenceTable);

                foreach (var i in value.DensePart)
                {
                    WriteValueBytes(i);
                }

                return;
            }
        }

        public void WriteBytes<TKey, TValue>(Amf3Dictionary<TKey, TValue> value)
        {
            _writerBuffer.WriteToBuffer((byte)Amf3Type.Dictionary);

            var refIndex = _objectReferenceTable.IndexOf(value);

            if (refIndex >= 0)
            {
                var header = (uint)refIndex << 1;
                WriteU29BytesImpl(header);
                return;
            }
            else
            {
                _objectReferenceTable.Add(value);
                var header = (uint)value.Count << 1 | 0x01;
                WriteU29BytesImpl(header);

                _writerBuffer.WriteToBuffer((byte)(value.WeakKeys ? 0x01 : 0x00));
                foreach ((var key, var item) in value)
                {
                    WriteValueBytes(key);
                    WriteValueBytes(item);
                }
                return;
            }
        }

    }
}
