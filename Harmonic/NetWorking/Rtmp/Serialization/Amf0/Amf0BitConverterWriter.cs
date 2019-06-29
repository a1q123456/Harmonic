using Harmonic.NetWorking.Rtmp.Common;
using Harmonic.NetWorking.Rtmp.Data;
using Harmonic.NetWorking.Rtmp.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace Harmonic.NetWorking.Rtmp.Serialization.Amf0
{
    public partial class Amf0BitConverter
    {
        private delegate bool GetBytesHandler<T>(T value, Span<byte> buffer, out int bytesConsumed);
        private delegate bool GetBytesHandler(object value, Span<byte> buffer, out int bytesConsumed);

        private IReadOnlyDictionary<Type, GetBytesHandler> _getBytesHandlers = null;

        private GetBytesHandler GetBytesWrapper<T>(GetBytesHandler<T> handler)
        {
            return (object v, Span<byte> b, out int bytesConsumed) =>
            {
                return handler((T)v, b, out bytesConsumed);
            };
        }

        public bool TryGetBytes(string str, Span<byte> buffer, out int consumed)
        {
            var bytesNeed = MARKER_LENGTH;
            var headerLength = 0;
            var bodyLength = 0;
            consumed = default;
            bool isLongString = false;


            bytesNeed += headerLength;
            bodyLength = Encoding.UTF8.GetByteCount(str);
            bytesNeed += bodyLength;

            if (bodyLength > ushort.MaxValue)
            {
                headerLength = LONG_STRING_HEADER_LENGTH;
                isLongString = true;
            }
            else
            {
                headerLength = STRING_HEADER_LENGTH;
            }

            if (bytesNeed > buffer.Length)
            {
                return false;
            }

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

            Encoding.UTF8.GetBytes(str, buffer.Slice(headerLength + MARKER_LENGTH));
            consumed = bytesNeed;
            return true;
        }

        public bool TryGetBytes(double val, Span<byte> buffer, out int consumed)
        {
            var bytesNeed = MARKER_LENGTH + sizeof(double);
            consumed = default;
            if (buffer.Length < bytesNeed)
            {
                return false;
            }

            buffer[0] = (byte)Amf0Type.Number;
            consumed = bytesNeed;
            return RtmpBitConverter.TryGetBytes(val, buffer.Slice(MARKER_LENGTH));
        }

        public bool TryGetBytes(bool val, Span<byte> buffer, out int consumed)
        {
            var bytesNeed = MARKER_LENGTH + sizeof(byte);
            consumed = default;
            if (buffer.Length < bytesNeed)
            {
                return false;
            }

            buffer[0] = (byte)Amf0Type.Boolean;
            buffer[1] = (byte)(val ? 1 : 0);
            consumed = bytesNeed;
            return true;
        }

        public bool TryGetBytes(Undefined value, Span<byte> buffer, out int consumed)
        {
            var bytesNeed = MARKER_LENGTH;
            consumed = default;
            if (buffer.Length < bytesNeed)
            {
                return false;
            }

            buffer[0] = (byte)Amf0Type.Undefined;
            consumed = bytesNeed;
            return true;
        }

        public bool TryGetBytes(Unsupported value, Span<byte> buffer, out int consumed)
        {
            var bytesNeed = MARKER_LENGTH;
            consumed = default;
            if (buffer.Length < bytesNeed)
            {
                return false;
            }

            buffer[0] = (byte)Amf0Type.Unsupported;
            consumed = bytesNeed;
            return true;
        }

        public bool TryGetReferenceIndexBytes(ushort index, Span<byte> buffer, out int consumed)
        {
            var bytesNeed = MARKER_LENGTH + sizeof(ushort);
            consumed = default;
            if (buffer.Length < bytesNeed)
            {
                return false;
            }

            buffer[0] = (byte)Amf0Type.Reference;
            consumed = bytesNeed;
            return RtmpBitConverter.TryGetBytes(index, buffer.Slice(MARKER_LENGTH));
        }

        public bool TryGetObjectEndBytes(Span<byte> buffer, out int consumed)
        {
            var bytesNeed = MARKER_LENGTH;
            consumed = default;
            if (buffer.Length < bytesNeed)
            {
                return false;
            }

            buffer[0] = (byte)Amf0Type.ObjectEnd;
            consumed = bytesNeed;
            return true;
        }

        public bool TryGetBytes(DateTime dateTime, Span<byte> buffer, out int consumed)
        {
            var bytesNeed = MARKER_LENGTH + sizeof(double) + sizeof(short);
            consumed = default;
            if (buffer.Length < bytesNeed)
            {
                return false;
            }
            buffer.Slice(0, bytesNeed).Clear();
            buffer[0] = (byte)Amf0Type.Date;
            var dof = new DateTimeOffset(dateTime);
            var timestamp = (double)dof.ToUnixTimeMilliseconds() / 1000;
            consumed = bytesNeed;
            return RtmpBitConverter.TryGetBytes(timestamp, buffer.Slice(MARKER_LENGTH));
        }

        public bool TryGetBytes(XmlDocument xml, Span<byte> buffer, out int consumed)
        {
            var bytesNeed = MARKER_LENGTH;
            string content = null;
            consumed = default;
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

            if (buffer.Length < bytesNeed)
            {
                return false;
            }

            buffer[0] = (byte)Amf0Type.XmlDocument;
            RtmpBitConverter.TryGetBytes((uint)bodyBytes, buffer.Slice(MARKER_LENGTH));
            Encoding.UTF8.GetBytes(content, buffer.Slice(MARKER_LENGTH + LONG_STRING_HEADER_LENGTH));
            consumed = bytesNeed;
            return true;
        }

        public bool TryGetNullBytes(Span<byte> buffer, out int consumed)
        {
            consumed = default;
            if (buffer.Length < MARKER_LENGTH)
            {
                return false;
            }
            buffer[0] = (byte)Amf0Type.Null;
            consumed = MARKER_LENGTH;
            return true;
        }

        public bool TryGetUndefinedBytes(Span<byte> buffer, out int consumed)
        {
            consumed = default;
            if (buffer.Length < MARKER_LENGTH)
            {
                return false;
            }
            buffer[0] = (byte)Amf0Type.Undefined;
            consumed = MARKER_LENGTH;
            return true;
        }

        public bool TryGetUnsupportedBytes(Span<byte> buffer, out int consumed)
        {
            consumed = default;
            if (buffer.Length < MARKER_LENGTH)
            {
                return false;
            }
            buffer[0] = (byte)Amf0Type.Unsupported;
            consumed = MARKER_LENGTH;
            return true;
        }

        public bool TryGetBytesGeneric(object value, Span<byte> buffer, out int bytesConsumed)
        {
            bytesConsumed = default;
            var valueType = value.GetType();
            if (!_getBytesHandlers.TryGetValue(valueType, out var handler))
            {
                return false;
            }

            return handler(value, buffer, out bytesConsumed);
        }

        public bool TryGetBytes(List<object> value, Span<byte> buffer, out int consumed)
        {
            consumed = 0;
            if (value == null)
            {
                throw new ArgumentNullException();
            }

            var bytesNeed = MARKER_LENGTH + sizeof(uint);
            if (buffer.Length < bytesNeed)
            {
                return false;
            }

            var refIndex = _writeReferenceTable.IndexOf(value);

            if (refIndex >= 0)
            {
                return TryGetReferenceIndexBytes((ushort)refIndex, buffer, out consumed);
            }

            buffer[0] = (byte)Amf0Type.StrictArray;
            if (!RtmpBitConverter.TryGetBytes((uint)value.Count, buffer.Slice(MARKER_LENGTH)))
            {
                return false;
            }
            consumed = bytesNeed;
            var arrayBuffer = buffer.Slice(bytesNeed);

            foreach (var data in value)
            {
                if (!TryGetBytesGeneric(data, arrayBuffer, out var valueConsumed))
                {
                    if (!TryGetBytes(data, arrayBuffer, out valueConsumed))
                    {
                        return false;
                    }
                }
                arrayBuffer = arrayBuffer.Slice(valueConsumed);
                consumed += valueConsumed;
            }
            _writeReferenceTable.Add(value);
            return true;
        }

        public bool TryGetBytes(Dictionary<string, object> value, Span<byte> buffer, out int consumed)
        {
            consumed = 0;
            if (value == null)
            {
                throw new ArgumentNullException();
            }

            if (buffer.Length < MARKER_LENGTH + sizeof(uint))
            {
                return false;
            }
            var refIndex = _writeReferenceTable.IndexOf(value);

            if (refIndex >= 0)
            {
                return TryGetReferenceIndexBytes((ushort)refIndex, buffer, out consumed);
            }
            buffer[0] = (byte)Amf0Type.EcmaArray;
            if (!RtmpBitConverter.TryGetBytes((uint)value.Count, buffer.Slice(MARKER_LENGTH)))
            {
                return false;
            }

            var arrayBuffer = buffer.Slice(MARKER_LENGTH + sizeof(uint));

            foreach ((var key, var data) in value)
            {
                if (!TryGetBytes(key, arrayBuffer, out var keyConsumed))
                {
                    return false;
                }
                arrayBuffer = arrayBuffer.Slice(keyConsumed);
                if (!TryGetBytesGeneric(data, arrayBuffer, out var valueConsumed))
                {
                    if (!TryGetBytes(data, arrayBuffer, out valueConsumed))
                    {
                        return false;
                    }
                }
                arrayBuffer = arrayBuffer.Slice(valueConsumed);
                consumed += keyConsumed + valueConsumed;
            }
            _writeReferenceTable.Add(value);
            return true;
        }

        public bool TryGetTypedBytes(object value, Span<byte> buffer, out int consumed)
        {
            consumed = 0;
            var bytesNeed = MARKER_LENGTH + sizeof(ushort);
            if (buffer.Length < bytesNeed)
            {
                return false;
            }
            if (value == null)
            {
                throw new ArgumentNullException();
            }
            var refIndex = _writeReferenceTable.IndexOf(value);

            if (refIndex >= 0)
            {
                return TryGetReferenceIndexBytes((ushort)refIndex, buffer, out consumed);
            }
            buffer[0] = (byte)Amf0Type.TypedObject;
            buffer = buffer.Slice(MARKER_LENGTH);
            var valueType = value.GetType();
            var classNameLength = (ushort)Encoding.UTF8.GetByteCount(valueType.Name);
            RtmpBitConverter.TryGetBytes(classNameLength, buffer);
            consumed += sizeof(ushort);
            buffer = buffer.Slice(classNameLength);
            consumed += classNameLength;
            if (!TryGetObjectBytesImpl(value, buffer.Slice(MARKER_LENGTH), out var bodyConsumed))
            {
                return false;
            }

            consumed += bodyConsumed;
            if (!TryGetBytes("", buffer, out var keyConsumed))
            {
                return false;
            }
            if (!TryGetObjectEndBytes(buffer, out var valueConsumed))
            {
                return false;
            }
            consumed += keyConsumed;
            consumed += valueConsumed;
            _writeReferenceTable.Add(value);
            return true;
        }

        public bool TryGetBytes(object value, Span<byte> buffer, out int consumed)
        {
            consumed = 0;
            if (buffer.Length < MARKER_LENGTH)
            {
                return false;
            }
            if (value == null)
            {
                return TryGetNullBytes(buffer, out consumed);
            }
            var refIndex = _writeReferenceTable.IndexOf(value);

            if (refIndex >= 0)
            {
                return TryGetReferenceIndexBytes((ushort)refIndex, buffer, out consumed);
            }
            buffer[0] = (byte)Amf0Type.Object;
            consumed = MARKER_LENGTH;
            if (!TryGetObjectBytesImpl(value, buffer.Slice(MARKER_LENGTH), out var bodyConsumed))
            {
                return false;
            }

            consumed += bodyConsumed;
            if (!TryGetBytes("", buffer, out var keyConsumed))
            {
                return false;
            }
            if (!TryGetObjectEndBytes(buffer, out var valueConsumed))
            {
                return false;
            }
            consumed += keyConsumed;
            consumed += valueConsumed;
            _writeReferenceTable.Add(value);
            return true;
        }

        private bool TryGetObjectBytesImpl(object value, Span<byte> buffer, out int consumed)
        {
            consumed = 0;

            var props = value.GetType().GetProperties();
            var propBuffer = buffer;
            foreach (var prop in props)
            {
                var propValue = prop.GetValue(value);
                if (!TryGetBytes(prop.Name, propBuffer, out var keyConsumed))
                {
                    return false;
                }
                propBuffer = propBuffer.Slice(keyConsumed);
                if (!TryGetBytesGeneric(propValue, propBuffer, out var valueConsumed))
                {
                    if (!TryGetObjectBytesImpl(propValue, propBuffer, out valueConsumed))
                    {
                        return false;
                    }
                }
                propBuffer = propBuffer.Slice(valueConsumed);
                consumed += keyConsumed + valueConsumed;
            }


            return true;
        }
    }
}
