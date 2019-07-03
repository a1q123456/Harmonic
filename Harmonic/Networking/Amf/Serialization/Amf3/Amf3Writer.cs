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

        public Amf3Writer()
        {

            var writeHandlers = new Dictionary<Type, WriteHandler>();
            writeHandlers[typeof(int)] = WriteHandlerWrapper<double>(WriteBytes);
            writeHandlers[typeof(uint)] = WriteHandlerWrapper<uint>(WriteU29Bytes);
            writeHandlers[typeof(long)] = WriteHandlerWrapper<double>(WriteBytes);
            writeHandlers[typeof(ulong)] = WriteHandlerWrapper<uint>(WriteU29Bytes);
            writeHandlers[typeof(short)] = WriteHandlerWrapper<double>(WriteBytes);
            writeHandlers[typeof(ushort)] = WriteHandlerWrapper<uint>(WriteU29Bytes);
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
            _writeHandlers = writeHandlers;
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
            else if (value <= 0x3FFFFFFF)
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
        }

        public void WriteU29Bytes(uint value)
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
            var timestamp = timeOffset.ToUnixTimeMilliseconds() / 1000.0d;
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
            var valueType = value.GetType();

            _writeHandlers.TryGetValue(valueType, out var handler);

            handler(value);
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
                    traits.ClassName = objType.Name;
                    traits.ClassType = Amf3ClassType.Typed;
                }
                traits.IsDynamic = amf3Object.IsDynamic;
                traits.Members = new List<string>(amf3Object.Fields.Keys);
                memberValues = new List<object>(amf3Object.Fields.Keys.Select(k => amf3Object.Fields[k]));
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
                var amf3Obj = value as Amf3Object;
                foreach ((var key, var item) in amf3Obj.DynamicFields)
                {
                    WriteStringBytesImpl(key, _stringReferenceTable);
                    WriteValueBytes(item);
                }
            }
        }

        public void WriteBytes(Vector<uint> value)
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
                var buffer = _arrayPool.Rent(sizeof(uint));
                try
                {
                    foreach (var i in value)
                    {
                        Contract.Assert(NetworkBitConverter.TryGetBytes(i, buffer));
                    }
                    _writerBuffer.WriteToBuffer(buffer.AsSpan(0, sizeof(uint)));
                }
                finally
                {
                    _arrayPool.Return(buffer);
                }
            }
        }

        public void WriteBytes(Vector<int> value)
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

                var className = typeof(T) == typeof(object) ? "*" : typeof(T).Name;
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
                var header = ((uint)value.DensePart.Count << 1) & 0x01;
                WriteU29BytesImpl(header);

                WriteStringBytesImpl("", _stringReferenceTable);
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
                var header = (uint)value.Count << 1;
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
