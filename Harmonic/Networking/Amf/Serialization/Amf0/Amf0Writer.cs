using Harmonic.Buffers;
using Harmonic.Networking.Amf.Attributes;
using Harmonic.Networking.Amf.Common;
using Harmonic.Networking.Rtmp.Serialization;
using Harmonic.Networking.Utils;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;

namespace Harmonic.Networking.Amf.Serialization.Amf0
{
    public class Amf0Writer
    {
        private delegate void GetBytesHandler<T>(T value);
        private delegate void GetBytesHandler(object value);
        private List<object> _referenceTable = new List<object>();
        private IReadOnlyDictionary<Type, GetBytesHandler> _getBytesHandlers = null;
        private UnlimitedBuffer _writeBuffer = new UnlimitedBuffer();
        private ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
        public int MessageLength => _writeBuffer.BufferLength;

        public Amf0Writer()
        {
            var getBytesHandlers = new Dictionary<Type, GetBytesHandler>();
            getBytesHandlers[typeof(double)] = GetBytesWrapper<double>(WriteBytes);
            getBytesHandlers[typeof(int)] = GetBytesWrapper<double>(WriteBytes);
            getBytesHandlers[typeof(short)] = GetBytesWrapper<double>(WriteBytes);
            getBytesHandlers[typeof(long)] = GetBytesWrapper<double>(WriteBytes);
            getBytesHandlers[typeof(uint)] = GetBytesWrapper<double>(WriteBytes);
            getBytesHandlers[typeof(ushort)] = GetBytesWrapper<double>(WriteBytes);
            getBytesHandlers[typeof(ulong)] = GetBytesWrapper<double>(WriteBytes);
            getBytesHandlers[typeof(float)] = GetBytesWrapper<double>(WriteBytes);
            getBytesHandlers[typeof(DateTime)] = GetBytesWrapper<DateTime>(WriteBytes);
            getBytesHandlers[typeof(string)] = GetBytesWrapper<string>(WriteBytes);
            getBytesHandlers[typeof(XmlDocument)] = GetBytesWrapper<XmlDocument>(WriteBytes);
            getBytesHandlers[typeof(Unsupported)] = GetBytesWrapper<Unsupported>(WriteBytes);
            getBytesHandlers[typeof(Undefined)] = GetBytesWrapper<Undefined>(WriteBytes);
            getBytesHandlers[typeof(bool)] = GetBytesWrapper<bool>(WriteBytes);
            getBytesHandlers[typeof(object)] = GetBytesWrapper<object>(WriteBytes);
            getBytesHandlers[typeof(List<object>)] = GetBytesWrapper<List<object>>(WriteBytes);
            _getBytesHandlers = getBytesHandlers;
        }


        private GetBytesHandler GetBytesWrapper<T>(GetBytesHandler<T> handler)
        {
            return (object v) =>
            {
                if (v is T tv)
                {
                    handler(tv);
                }
                else
                {
                    handler((T)Convert.ChangeType(v, typeof(T)));
                }
            };
        }

        public void GetMessage(Span<byte> buffer)
        {
            _referenceTable.Clear();
            _writeBuffer.TakeOutMemory(buffer);
        }

        public bool TryGetAvmPlusBytes()
        {
            _writeBuffer.WriteToBuffer((byte)Amf0Type.AvmPlusObject);
            return true;
        }

        private void WriteStringBytesImpl(string str, out bool isLongString, bool marker = false, bool forceLongString = false)
        {
            var bytesNeed = 0;
            var headerLength = 0;
            var bodyLength = 0;

            bodyLength = Encoding.UTF8.GetByteCount(str);
            bytesNeed += bodyLength;

            if (bodyLength > ushort.MaxValue || forceLongString)
            {
                headerLength = Amf0CommonValues.LONG_STRING_HEADER_LENGTH;
                isLongString = true;
                if (marker)
                {
                    _writeBuffer.WriteToBuffer((byte)Amf0Type.LongString);
                }

            }
            else
            {
                isLongString = false;
                headerLength = Amf0CommonValues.STRING_HEADER_LENGTH;
                if (marker)
                {
                    _writeBuffer.WriteToBuffer((byte)Amf0Type.String);
                }
            }
            bytesNeed += headerLength;
            var bufferBackend = _arrayPool.Rent(bytesNeed);
            try
            {
                var buffer = bufferBackend.AsSpan(0, bytesNeed);
                if (isLongString)
                {
                    Contract.Assert(NetworkBitConverter.TryGetBytes((uint)bodyLength, buffer));
                }
                else
                {
                    Contract.Assert(NetworkBitConverter.TryGetBytes((ushort)bodyLength, buffer));
                }

                Encoding.UTF8.GetBytes(str, buffer.Slice(headerLength));

                _writeBuffer.WriteToBuffer(buffer);
            }
            finally
            {
                _arrayPool.Return(bufferBackend);
            }
            
        }

        public void WriteBytes(string str)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH;

            var refIndex = _referenceTable.IndexOf(str);

            if (refIndex != -1)
            {
                WriteReferenceIndexBytes((ushort)refIndex);
                return;
            }

            WriteStringBytesImpl(str, out var isLongString, true);
            _referenceTable.Add(str);
        }

        public void WriteBytes(double val)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH + sizeof(double);
            var bufferBackend = _arrayPool.Rent(bytesNeed);
            try
            {
                var buffer = bufferBackend.AsSpan(0, bytesNeed);
                buffer[0] = (byte)Amf0Type.Number;
                Contract.Assert(NetworkBitConverter.TryGetBytes(val, buffer.Slice(Amf0CommonValues.MARKER_LENGTH)));
                _writeBuffer.WriteToBuffer(buffer);
            }
            finally
            {
                _arrayPool.Return(bufferBackend);
            }
        }

        public void WriteBytes(bool val)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH + sizeof(byte);

            _writeBuffer.WriteToBuffer((byte)Amf0Type.Boolean);
            _writeBuffer.WriteToBuffer((byte)(val ? 1 : 0));

        }

        public void WriteBytes(Undefined value)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH;
            var bufferBackend = _arrayPool.Rent(bytesNeed);

            _writeBuffer.WriteToBuffer((byte)Amf0Type.Undefined);
        }

        public void WriteBytes(Unsupported value)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH;
            _writeBuffer.WriteToBuffer((byte)Amf0Type.Unsupported);
        }

        private void WriteReferenceIndexBytes(ushort index)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH + sizeof(ushort);
            var backend = _arrayPool.Rent(bytesNeed);
            try
            {
                var buffer = backend.AsSpan(0, bytesNeed);
                buffer[0] = (byte)Amf0Type.Reference;
                Contract.Assert(NetworkBitConverter.TryGetBytes(index, buffer.Slice(Amf0CommonValues.MARKER_LENGTH)));
                _writeBuffer.WriteToBuffer(buffer);
            }
            finally
            {
                _arrayPool.Return(backend);
            }

        }

        private void WriteObjectEndBytes()
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH;
            _writeBuffer.WriteToBuffer((byte)Amf0Type.ObjectEnd);
        }

        public void WriteBytes(DateTime dateTime)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH + sizeof(double) + sizeof(short);

            var backend = _arrayPool.Rent(bytesNeed);
            try
            {
                var buffer = backend.AsSpan(0, bytesNeed);
                buffer.Slice(0, bytesNeed).Clear();
                buffer[0] = (byte)Amf0Type.Date;
                var dof = new DateTimeOffset(dateTime);
                var timestamp = (double)dof.ToUnixTimeMilliseconds();
                Contract.Assert(NetworkBitConverter.TryGetBytes(timestamp, buffer.Slice(Amf0CommonValues.MARKER_LENGTH)));
                _writeBuffer.WriteToBuffer(buffer);
            }
            finally
            {
                _arrayPool.Return(backend);
            }

        }

        public void WriteBytes(XmlDocument xml)
        {
            string content = null;
            using (var stringWriter = new StringWriter())
            using (var xmlTextWriter = XmlWriter.Create(stringWriter))
            {
                xml.WriteTo(xmlTextWriter);
                xmlTextWriter.Flush();
                content = stringWriter.GetStringBuilder().ToString();
            }

            _writeBuffer.WriteToBuffer((byte)Amf0Type.XmlDocument);
            WriteStringBytesImpl(content, out _, forceLongString: true);
        }

        public void WriteNullBytes()
        {
            _writeBuffer.WriteToBuffer((byte)Amf0Type.Null);
        }

        public void WriteValueBytes(object value)
        {
            var valueType = value != null ? value.GetType() : typeof(object);
            Contract.Assert(_getBytesHandlers.TryGetValue(valueType, out var handler));

            handler(value);
        }

        // strict array
        public void WriteBytes(List<object> value)
        {
            if (value == null)
            {
                WriteNullBytes();
                return;
            }

            var bytesNeed = Amf0CommonValues.MARKER_LENGTH + sizeof(uint);

            var refIndex = _referenceTable.IndexOf(value);

            if (refIndex >= 0)
            {
                WriteReferenceIndexBytes((ushort)refIndex);
                return;
            }
            _referenceTable.Add(value);

            _writeBuffer.WriteToBuffer((byte)Amf0Type.StrictArray);
            var countBuffer = _arrayPool.Rent(sizeof(uint));
            try
            {
                Contract.Assert(NetworkBitConverter.TryGetBytes((uint)value.Count, countBuffer));
                _writeBuffer.WriteToBuffer(countBuffer.AsSpan(0, sizeof(uint)));
            }
            finally
            {
                _arrayPool.Return(countBuffer);
            }

            foreach (var data in value)
            {
                WriteValueBytes(data);
            }
        }

        public void WriteBytes(Dictionary<string, object> value)
        {
            if (value == null)
            {
                WriteNullBytes();
                return;
            }

            var refIndex = _referenceTable.IndexOf(value);

            if (refIndex >= 0)
            {
                WriteReferenceIndexBytes((ushort)refIndex);
                return;
            }
            _writeBuffer.WriteToBuffer((byte)Amf0Type.EcmaArray);
            _referenceTable.Add(value);
            var countBuffer = _arrayPool.Rent(sizeof(uint));
            try
            {
                Contract.Assert(NetworkBitConverter.TryGetBytes((uint)value.Count, countBuffer));
                _writeBuffer.WriteToBuffer(countBuffer.AsSpan(0, sizeof(uint)));
            }
            finally
            {
                _arrayPool.Return(countBuffer);
            }

            foreach ((var key, var data) in value)
            {
                WriteStringBytesImpl(key, out _);
                WriteValueBytes(data);
            }
            WriteStringBytesImpl("", out _);
            WriteObjectEndBytes();
        }

        public void WriteTypedBytes(object value)
        {
            if (value == null)
            {
                WriteNullBytes();
                return;
            }
            var refIndex = _referenceTable.IndexOf(value);

            if (refIndex >= 0)
            {
                WriteReferenceIndexBytes((ushort)refIndex);
                return;
            }
            _writeBuffer.WriteToBuffer((byte)Amf0Type.TypedObject);
            _referenceTable.Add(value);

            var valueType = value.GetType();
            var className = valueType.Name;

            var clsAttr = (TypedObjectAttribute)Attribute.GetCustomAttribute(valueType, typeof(TypedObjectAttribute));
            if (clsAttr != null && clsAttr.Name != null)
            {
                className = clsAttr.Name;
            }

            WriteStringBytesImpl(className, out _);

            var props = valueType.GetProperties();
            
            foreach (var prop in props)
            {
                var attr = (ClassFieldAttribute)Attribute.GetCustomAttribute(prop, typeof(ClassFieldAttribute));
                if (attr != null)
                {
                    WriteStringBytesImpl(attr.Name ?? prop.Name, out _);
                    WriteValueBytes(prop.GetValue(value));
                }
            }

            WriteStringBytesImpl("", out _);
            WriteObjectEndBytes();
        }

        public void WriteBytes(object value)
        {
            if (value == null)
            {
                WriteNullBytes();
                return;
            }
            var refIndex = _referenceTable.IndexOf(value);

            if (refIndex >= 0)
            {
                WriteReferenceIndexBytes((ushort)refIndex);
                return;
            }
            _writeBuffer.WriteToBuffer((byte)Amf0Type.Object);
            _referenceTable.Add(value);

            WriteObjectBytesImpl(value);
            WriteStringBytesImpl("", out _);
            WriteObjectEndBytes();
        }

        private void WriteObjectBytesImpl(object value)
        {
            var props = value.GetType().GetProperties();
            foreach (var prop in props)
            {
                var propValue = prop.GetValue(value);
                WriteStringBytesImpl(prop.Name, out _);
                WriteValueBytes(propValue);
            }
        }
    }
}
