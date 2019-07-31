using Harmonic.Buffers;
using Harmonic.Networking.Amf.Attributes;
using Harmonic.Networking.Amf.Common;
using Harmonic.Networking.Amf.Serialization.Attributes;
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
        private delegate void GetBytesHandler<T>(T value, SerializationContext context);
        private delegate void GetBytesHandler(object value, SerializationContext context);
        private IReadOnlyDictionary<Type, GetBytesHandler> _getBytesHandlers = null;
        private ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;

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
            getBytesHandlers[typeof(object)] = GetBytesWrapper<object>(WriteTypedBytes);
            getBytesHandlers[typeof(AmfObject)] = GetBytesWrapper<AmfObject>(WriteBytes);
            getBytesHandlers[typeof(Dictionary<string, object>)] = GetBytesWrapper<Dictionary<string, object>>(WriteBytes);
            getBytesHandlers[typeof(List<object>)] = GetBytesWrapper<List<object>>(WriteBytes);
            _getBytesHandlers = getBytesHandlers;
        }


        private GetBytesHandler GetBytesWrapper<T>(GetBytesHandler<T> handler)
        {
            return (object v, SerializationContext context) =>
            {
                if (v is T tv)
                {
                    handler(tv, context);
                }
                else
                {
                    handler((T)Convert.ChangeType(v, typeof(T)), context);
                }
            };
        }

        public void WriteAvmPlusBytes(SerializationContext context)
        {
            context.Buffer.WriteToBuffer((byte)Amf0Type.AvmPlusObject);
        }

        private void WriteStringBytesImpl(string str, SerializationContext context, out bool isLongString, bool marker = false, bool forceLongString = false)
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
                    context.Buffer.WriteToBuffer((byte)Amf0Type.LongString);
                }

            }
            else
            {
                isLongString = false;
                headerLength = Amf0CommonValues.STRING_HEADER_LENGTH;
                if (marker)
                {
                    context.Buffer.WriteToBuffer((byte)Amf0Type.String);
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

                context.Buffer.WriteToBuffer(buffer);
            }
            finally
            {
                _arrayPool.Return(bufferBackend);
            }

        }

        public void WriteBytes(string str, SerializationContext context)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH;

            var refIndex = context.ReferenceTable.IndexOf(str);

            if (refIndex != -1)
            {
                WriteReferenceIndexBytes((ushort)refIndex, context);
                return;
            }

            WriteStringBytesImpl(str, context, out var isLongString, true);
            context.ReferenceTable.Add(str);
        }

        public void WriteBytes(double val, SerializationContext context)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH + sizeof(double);
            var bufferBackend = _arrayPool.Rent(bytesNeed);
            try
            {
                var buffer = bufferBackend.AsSpan(0, bytesNeed);
                buffer[0] = (byte)Amf0Type.Number;
                Contract.Assert(NetworkBitConverter.TryGetBytes(val, buffer.Slice(Amf0CommonValues.MARKER_LENGTH)));
                context.Buffer.WriteToBuffer(buffer);
            }
            finally
            {
                _arrayPool.Return(bufferBackend);
            }
        }

        public void WriteBytes(bool val, SerializationContext context)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH + sizeof(byte);

            context.Buffer.WriteToBuffer((byte)Amf0Type.Boolean);
            context.Buffer.WriteToBuffer((byte)(val ? 1 : 0));

        }

        public void WriteBytes(Undefined value, SerializationContext context)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH;
            var bufferBackend = _arrayPool.Rent(bytesNeed);

            context.Buffer.WriteToBuffer((byte)Amf0Type.Undefined);
        }

        public void WriteBytes(Unsupported value, SerializationContext context)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH;
            context.Buffer.WriteToBuffer((byte)Amf0Type.Unsupported);
        }

        private void WriteReferenceIndexBytes(ushort index, SerializationContext context)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH + sizeof(ushort);
            var backend = _arrayPool.Rent(bytesNeed);
            try
            {
                var buffer = backend.AsSpan(0, bytesNeed);
                buffer[0] = (byte)Amf0Type.Reference;
                Contract.Assert(NetworkBitConverter.TryGetBytes(index, buffer.Slice(Amf0CommonValues.MARKER_LENGTH)));
                context.Buffer.WriteToBuffer(buffer);
            }
            finally
            {
                _arrayPool.Return(backend);
            }

        }

        private void WriteObjectEndBytes(SerializationContext context)
        {
            var bytesNeed = Amf0CommonValues.MARKER_LENGTH;
            context.Buffer.WriteToBuffer((byte)Amf0Type.ObjectEnd);
        }

        public void WriteBytes(DateTime dateTime, SerializationContext context)
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
                context.Buffer.WriteToBuffer(buffer);
            }
            finally
            {
                _arrayPool.Return(backend);
            }

        }

        public void WriteBytes(XmlDocument xml, SerializationContext context)
        {
            string content = null;
            using (var stringWriter = new StringWriter())
            using (var xmlTextWriter = XmlWriter.Create(stringWriter))
            {
                xml.WriteTo(xmlTextWriter);
                xmlTextWriter.Flush();
                content = stringWriter.GetStringBuilder().ToString();
            }

            context.Buffer.WriteToBuffer((byte)Amf0Type.XmlDocument);
            WriteStringBytesImpl(content, context, out _, forceLongString: true);
        }

        public void WriteNullBytes(SerializationContext context)
        {
            context.Buffer.WriteToBuffer((byte)Amf0Type.Null);
        }

        public void WriteValueBytes(object value, SerializationContext context)
        {
            var valueType = value != null ? value.GetType() : typeof(object);
            if (!_getBytesHandlers.TryGetValue(valueType, out var handler))
            {
                throw new InvalidOperationException();
            }


            handler(value, context);
        }

        // strict array
        public void WriteBytes(List<object> value, SerializationContext context)
        {
            if (value == null)
            {
                WriteNullBytes(context);
                return;
            }

            var bytesNeed = Amf0CommonValues.MARKER_LENGTH + sizeof(uint);

            var refIndex = context.ReferenceTable.IndexOf(value);

            if (refIndex >= 0)
            {
                WriteReferenceIndexBytes((ushort)refIndex, context);
                return;
            }
            context.ReferenceTable.Add(value);

            context.Buffer.WriteToBuffer((byte)Amf0Type.StrictArray);
            var countBuffer = _arrayPool.Rent(sizeof(uint));
            try
            {
                Contract.Assert(NetworkBitConverter.TryGetBytes((uint)value.Count, countBuffer));
                context.Buffer.WriteToBuffer(countBuffer.AsSpan(0, sizeof(uint)));
            }
            finally
            {
                _arrayPool.Return(countBuffer);
            }

            foreach (var data in value)
            {
                WriteValueBytes(data, context);
            }
        }

        // ecma array
        public void WriteBytes(Dictionary<string, object> value, SerializationContext context)
        {
            if (value == null)
            {
                WriteNullBytes(context);
                return;
            }

            var refIndex = context.ReferenceTable.IndexOf(value);

            if (refIndex >= 0)
            {
                WriteReferenceIndexBytes((ushort)refIndex, context);
                return;
            }
            context.Buffer.WriteToBuffer((byte)Amf0Type.EcmaArray);
            context.ReferenceTable.Add(value);
            var countBuffer = _arrayPool.Rent(sizeof(uint));
            try
            {
                Contract.Assert(NetworkBitConverter.TryGetBytes((uint)value.Count, countBuffer));
                context.Buffer.WriteToBuffer(countBuffer.AsSpan(0, sizeof(uint)));
            }
            finally
            {
                _arrayPool.Return(countBuffer);
            }

            foreach ((var key, var data) in value)
            {
                WriteStringBytesImpl(key, context, out _);
                WriteValueBytes(data, context);
            }
            WriteStringBytesImpl("", context, out _);
            WriteObjectEndBytes(context);
        }

        public void WriteTypedBytes(object value, SerializationContext context)
        {
            if (value == null)
            {
                WriteNullBytes(context);
                return;
            }
            var refIndex = context.ReferenceTable.IndexOf(value);

            if (refIndex >= 0)
            {
                WriteReferenceIndexBytes((ushort)refIndex, context);
                return;
            }
            context.Buffer.WriteToBuffer((byte)Amf0Type.TypedObject);
            context.ReferenceTable.Add(value);

            var valueType = value.GetType();
            var className = valueType.Name;

            var clsAttr = (TypedObjectAttribute)Attribute.GetCustomAttribute(valueType, typeof(TypedObjectAttribute));
            if (clsAttr != null && clsAttr.Name != null)
            {
                className = clsAttr.Name;
            }

            WriteStringBytesImpl(className, context, out _);

            var props = valueType.GetProperties();

            foreach (var prop in props)
            {
                var attr = (ClassFieldAttribute)Attribute.GetCustomAttribute(prop, typeof(ClassFieldAttribute));
                if (attr != null)
                {
                    WriteStringBytesImpl(attr.Name ?? prop.Name, context, out _);
                    WriteValueBytes(prop.GetValue(value), context);
                }
            }

            WriteStringBytesImpl("", context, out _);
            WriteObjectEndBytes(context);
        }

        public void WriteBytes(AmfObject value, SerializationContext context)
        {
            if (value == null)
            {
                WriteNullBytes(context);
                return;
            }
            var refIndex = context.ReferenceTable.IndexOf(value);

            if (refIndex >= 0)
            {
                WriteReferenceIndexBytes((ushort)refIndex, context);
                return;
            }
            context.Buffer.WriteToBuffer((byte)Amf0Type.Object);
            context.ReferenceTable.Add(value);

            foreach (var field in value.Fields)
            {
                WriteStringBytesImpl(field.Key, context, out _);
                WriteValueBytes(field.Value, context);
            }

            foreach (var field in value.DynamicFields)
            {
                WriteStringBytesImpl(field.Key, context, out _);
                WriteValueBytes(field.Value, context);
            }

            WriteStringBytesImpl("", context, out _);
            WriteObjectEndBytes(context);
        }


    }
}
