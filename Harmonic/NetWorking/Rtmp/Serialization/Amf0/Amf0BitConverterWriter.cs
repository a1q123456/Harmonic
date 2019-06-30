using Harmonic.Buffers;
using Harmonic.NetWorking.Rtmp.Common;
using Harmonic.NetWorking.Rtmp.Data;
using Harmonic.NetWorking.Rtmp.Serialization;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Xml;

namespace Harmonic.NetWorking.Rtmp.Serialization.Amf0
{
    public class Amf0Writer
    {
        private delegate bool GetBytesHandler<T>(T value);
        private delegate bool GetBytesHandler(object value);
        private List<object> _referenceTable = new List<object>();
        private IReadOnlyDictionary<Type, GetBytesHandler> _getBytesHandlers = null;
        private UnlimitedBuffer _writeBuffer = new UnlimitedBuffer();
        private ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
        public int MessageLength => _writeBuffer.BufferLength;

        public Amf0Writer()
        {
            var getBytesHandlers = new Dictionary<Type, GetBytesHandler>();
            getBytesHandlers[typeof(double)] = GetBytesWrapper<double>(TryGetBytes);
            getBytesHandlers[typeof(int)] = GetBytesWrapper<double>(TryGetBytes);
            getBytesHandlers[typeof(short)] = GetBytesWrapper<double>(TryGetBytes);
            getBytesHandlers[typeof(long)] = GetBytesWrapper<double>(TryGetBytes);
            getBytesHandlers[typeof(uint)] = GetBytesWrapper<double>(TryGetBytes);
            getBytesHandlers[typeof(ushort)] = GetBytesWrapper<double>(TryGetBytes);
            getBytesHandlers[typeof(ulong)] = GetBytesWrapper<double>(TryGetBytes);
            getBytesHandlers[typeof(float)] = GetBytesWrapper<double>(TryGetBytes);
            getBytesHandlers[typeof(DateTime)] = GetBytesWrapper<DateTime>(TryGetBytes);
            getBytesHandlers[typeof(string)] = GetBytesWrapper<string>(TryGetBytes);
            getBytesHandlers[typeof(XmlDocument)] = GetBytesWrapper<XmlDocument>(TryGetBytes);
            getBytesHandlers[typeof(Unsupported)] = GetBytesWrapper<Unsupported>(TryGetBytes);
            getBytesHandlers[typeof(Undefined)] = GetBytesWrapper<Undefined>(TryGetBytes);
            getBytesHandlers[typeof(bool)] = GetBytesWrapper<bool>(TryGetBytes);
            getBytesHandlers[typeof(object)] = GetBytesWrapper<object>(TryGetBytes);
            _getBytesHandlers = getBytesHandlers;
        }


        private GetBytesHandler GetBytesWrapper<T>(GetBytesHandler<T> handler)
        {
            return (object v) =>
            {
                return handler((T)v);
            };
        }

        public void GetMessage(Span<byte> buffer)
        {
            _referenceTable.Clear();
            _writeBuffer.TakeOutMemory(buffer);
        }

        public bool TryGetBytes(string str)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH;
            var headerLength = 0;
            var bodyLength = 0;
            bool isLongString = false;


            bytesNeed += headerLength;
            bodyLength = Encoding.UTF8.GetByteCount(str);
            bytesNeed += bodyLength;

            if (bodyLength > ushort.MaxValue)
            {
                headerLength = Amf0CommonValues.LONG_STRING_HEADER_LENGTH;
                isLongString = true;
            }
            else
            {
                headerLength = Amf0CommonValues.STRING_HEADER_LENGTH;
            }

            var bufferBackend = _arrayPool.Rent(bytesNeed);
            try
            {
                var buffer = bufferBackend.AsSpan(0, bytesNeed);
                if (isLongString)
                {
                    buffer[0] = (byte)Amf0Type.LongString;

                    if (!RtmpBitConverter.TryGetBytes((uint)bodyLength, buffer.Slice(0, headerLength)))
                    {
                        return false;
                    }
                }
                else
                {
                    buffer[0] = (byte)Amf0Type.StrictArray;
                    if (!RtmpBitConverter.TryGetBytes((ushort)bodyLength, buffer.Slice(0, headerLength)))
                    {
                        return false;
                    }
                }

                Encoding.UTF8.GetBytes(str, buffer.Slice(headerLength + Amf0CommonValues.MARKER_LENGTH));

                _writeBuffer.WriteToBuffer(buffer);
            }
            finally
            {
                _arrayPool.Return(bufferBackend);
            }

            return true;
        }

        public bool TryGetBytes(double val)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH + sizeof(double);
            var bufferBackend = _arrayPool.Rent(bytesNeed);
            try
            {
                var buffer = bufferBackend.AsSpan(0, bytesNeed);
                buffer[0] = (byte)Amf0Type.Number;
                var ret = RtmpBitConverter.TryGetBytes(val, buffer.Slice(Amf0CommonValues.MARKER_LENGTH));
                _writeBuffer.WriteToBuffer(buffer);
                return ret;
            }
            finally
            {
                _arrayPool.Return(bufferBackend);
            }
        }

        public bool TryGetBytes(bool val)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH + sizeof(byte);

            _writeBuffer.WriteToBuffer((byte)Amf0Type.Boolean);
            _writeBuffer.WriteToBuffer((byte)(val ? 1 : 0));

            return true;

        }

        public bool TryGetBytes(Undefined value)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH;
            var bufferBackend = _arrayPool.Rent(bytesNeed);

            _writeBuffer.WriteToBuffer((byte)Amf0Type.Undefined);
            return true;

        }

        public bool TryGetBytes(Unsupported value)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH;
            _writeBuffer.WriteToBuffer((byte)Amf0Type.Unsupported);

            return true;
        }

        private bool TryGetReferenceIndexBytes(ushort index)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH + sizeof(ushort);
            var backend = _arrayPool.Rent(bytesNeed);
            try
            {
                var buffer = backend.AsSpan(0, bytesNeed);
                buffer[0] = (byte)Amf0Type.Reference;
                var ret = RtmpBitConverter.TryGetBytes(index, buffer.Slice(Amf0CommonValues.MARKER_LENGTH));
                _writeBuffer.WriteToBuffer(buffer);
                return ret;
            }
            finally
            {
                _arrayPool.Return(backend);
            }

        }

        public bool TryGetObjectEndBytes()
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH;
            _writeBuffer.WriteToBuffer((byte)Amf0Type.ObjectEnd);
            return true;
        }

        public bool TryGetBytes(DateTime dateTime)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH + sizeof(double) + sizeof(short);

            var backend = _arrayPool.Rent(bytesNeed);
            try
            {
                var buffer = backend.AsSpan(0, bytesNeed);
                buffer.Slice(0, bytesNeed).Clear();
                buffer[0] = (byte)Amf0Type.Date;
                var dof = new DateTimeOffset(dateTime);
                var timestamp = (double)dof.ToUnixTimeMilliseconds() / 1000;
                if (!RtmpBitConverter.TryGetBytes(timestamp, buffer.Slice(Amf0CommonValues.MARKER_LENGTH)))
                {
                    return false;
                }
                _writeBuffer.WriteToBuffer(buffer);
                return true;
            }
            finally
            {
                _arrayPool.Return(backend);
            }

        }

        public bool TryGetBytes(XmlDocument xml)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH;
            string content = null;
            using (var stringWriter = new StringWriter())
            using (var xmlTextWriter = XmlWriter.Create(stringWriter))
            {
                xml.WriteTo(xmlTextWriter);
                xmlTextWriter.Flush();
                content = stringWriter.GetStringBuilder().ToString();
            }

            if (content == null)
            {
                return false;
            }

            var bodyBytes = Encoding.UTF8.GetByteCount(content);
            bytesNeed += bodyBytes;

            var backend = _arrayPool.Rent(bytesNeed);
            try
            {
                var buffer = backend.AsSpan(0, bytesNeed);
                buffer[0] = (byte)Amf0Type.XmlDocument;
                RtmpBitConverter.TryGetBytes((uint)bodyBytes, buffer.Slice(Amf0CommonValues.MARKER_LENGTH));
                Encoding.UTF8.GetBytes(content, buffer.Slice(Amf0CommonValues.MARKER_LENGTH + Amf0CommonValues.LONG_STRING_HEADER_LENGTH));
                _writeBuffer.WriteToBuffer(buffer);
                return true;
            }
            finally
            {
                _arrayPool.Return(backend);
            }
        }

        public bool TryGetNullBytes()
        {
            _writeBuffer.WriteToBuffer((byte)Amf0Type.Null);

            return true;
        }

        public bool TryGetValueBytes(object value)
        {
            var valueType = value.GetType();
            if (!_getBytesHandlers.TryGetValue(valueType, out var handler))
            {
                return false;
            }

            return handler(value);
        }

        // strict array
        public bool TryGetBytes(List<object> value)
        {
            if (value == null)
            {
                return TryGetNullBytes();
            }

            var bytesNeed = Amf0CommonValues.MARKER_LENGTH + sizeof(uint);

            var refIndex = _referenceTable.IndexOf(value);

            if (refIndex >= 0)
            {
                return TryGetReferenceIndexBytes((ushort)refIndex);
            }

            _writeBuffer.WriteToBuffer((byte)Amf0Type.StrictArray);
            var countBuffer = _arrayPool.Rent(sizeof(uint));
            try
            {
                if (!RtmpBitConverter.TryGetBytes((uint)value.Count, countBuffer))
                {
                    return false;
                }
                _writeBuffer.WriteToBuffer(countBuffer.AsSpan(0, sizeof(uint)));
            }
            finally
            {
                _arrayPool.Return(countBuffer);
            }

            foreach (var data in value)
            {
                if (!TryGetValueBytes(data))
                {
                    return false;
                }
            }
            _referenceTable.Add(value);
            return true;
        }

        public bool TryGetBytes(Dictionary<string, object> value)
        {
            if (value == null)
            {
                return TryGetNullBytes();
            }

            var refIndex = _referenceTable.IndexOf(value);

            if (refIndex >= 0)
            {
                return TryGetReferenceIndexBytes((ushort)refIndex);
            }
            _writeBuffer.WriteToBuffer((byte)Amf0Type.EcmaArray);
            var countBuffer = _arrayPool.Rent(sizeof(uint));
            try
            {
                if (!RtmpBitConverter.TryGetBytes((uint)value.Count, countBuffer))
                {
                    return false;
                }
                _writeBuffer.WriteToBuffer(countBuffer.AsSpan(0, sizeof(uint)));
            }
            finally
            {
                _arrayPool.Return(countBuffer);
            }

            foreach ((var key, var data) in value)
            {
                if (!TryGetBytes(key))
                {
                    return false;
                }
                if (!TryGetValueBytes(data))
                {
                    return false;
                }
            }
            _referenceTable.Add(value);
            return true;
        }

        public bool TryGetTypedBytes(object value)
        {
            if (value == null)
            {
                return TryGetNullBytes();
            }
            var refIndex = _referenceTable.IndexOf(value);

            if (refIndex >= 0)
            {
                return TryGetReferenceIndexBytes((ushort)refIndex);
            }
            _writeBuffer.WriteToBuffer((byte)Amf0Type.TypedObject);

            var valueType = value.GetType();
            var classNameLength = (ushort)Encoding.UTF8.GetByteCount(valueType.Name);
            var countBuffer = _arrayPool.Rent(sizeof(ushort));
            try
            {
                RtmpBitConverter.TryGetBytes(classNameLength, countBuffer);
                _writeBuffer.WriteToBuffer(countBuffer.AsSpan(0, sizeof(ushort)));
            }
            finally
            {
                _arrayPool.Return(countBuffer);
            }

            if (!TryGetObjectBytesImpl(value))
            {
                return false;
            }
            
            if (!TryGetBytes(""))
            {
                return false;
            }
            if (!TryGetObjectEndBytes())
            {
                return false;
            }
            _referenceTable.Add(value);
            return true;
        }

        public bool TryGetBytes(object value)
        {
            if (value == null)
            {
                return TryGetNullBytes();
            }
            var refIndex = _referenceTable.IndexOf(value);

            if (refIndex >= 0)
            {
                return TryGetReferenceIndexBytes((ushort)refIndex);
            }
            _writeBuffer.WriteToBuffer((byte)Amf0Type.Object);

            if (!TryGetObjectBytesImpl(value))
            {
                return false;
            }
            
            if (!TryGetBytes(""))
            {
                return false;
            }
            if (!TryGetObjectEndBytes())
            {
                return false;
            }
            _referenceTable.Add(value);
            return true;
        }

        private bool TryGetObjectBytesImpl(object value)
        {
            var props = value.GetType().GetProperties();
            foreach (var prop in props)
            {
                var propValue = prop.GetValue(value);
                if (!TryGetBytes(prop.Name))
                {
                    return false;
                }
                if (!TryGetValueBytes(propValue))
                {
                    if (!TryGetObjectBytesImpl(propValue))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
