using Harmonic.NetWorking.Rtmp.Common;
using Harmonic.NetWorking.Rtmp.Data;
using System;
using System.Linq;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace Harmonic.NetWorking.Rtmp.Serialization.Amf3
{
    public partial class Amf3BitConverter
    {
        private delegate bool WriteHandler<T>(T value, Span<byte> buffer, out int consumed);
        private delegate bool WriteHandler(object value, Span<byte> buffer, out int consumed);


        private List<string> _writeStringReferenceTable = new List<string>();
        private List<object> _writeObjectReferenceTable = new List<object>();
        private List<object> _writeObjectTraitsReferenceTable = new List<object>();

        private Dictionary<Type, WriteHandler> _writeHandlers = new Dictionary<Type, WriteHandler>();

        private WriteHandler WriteHandlerWrapper<T>(WriteHandler<T> handler)
        {
            return (object obj, Span<byte> buffer, out int consumed) =>
            {
                consumed = default;
                return handler((T)obj, buffer, out consumed);
            };
        }

        public bool TryGetBytes(Undefined value, Span<byte> buffer, out int consumed)
        {
            consumed = default;
            if (buffer.Length < MARKER_LENGTH)
            {
                return false;
            }

            buffer[0] = (byte)Amf3Type.Undefined;
            consumed = MARKER_LENGTH;
            return true;

        }

        public bool TryGetBytes(bool value, Span<byte> buffer, out int consumed)
        {
            consumed = default;
            if (buffer.Length < MARKER_LENGTH)
            {
                return false;
            }

            if (value)
            {
                buffer[0] = (byte)Amf3Type.True;
            }
            else
            {
                buffer[0] = (byte)Amf3Type.False;
            }
            consumed = MARKER_LENGTH;
            return true;

        }

        private bool TryGetU29BytesImpl(uint value, Span<byte> buffer, out int consumed)
        {
            consumed = default;
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
            if (buffer.Length < length)
            {
                return false;
            }
            var arr = ArrayPool<byte>.Shared.Rent(4);
            try
            {
                RtmpBitConverter.TryGetBytes(value, arr);
                buffer[0] = arr[0];
                for (int i = 1; i < length; i++)
                {
                    buffer[i] = (byte)((arr[i] << i - 1) | (arr[i - 1] >> 9 - i));
                }

            }
            finally
            {
                ArrayPool<byte>.Shared.Return(arr);
            }
            consumed = length;
            return true;
        }

        public bool TryGetU29Bytes(uint value, Span<byte> buffer, out int consumed)
        {
            consumed = default;
            if (buffer.Length < MARKER_LENGTH)
            {
                return false;
            }
            buffer[0] = (byte)Amf3Type.Integer;
            if (!TryGetU29BytesImpl(value, buffer.Slice(MARKER_LENGTH), out var integerConsumed))
            {
                return false;
            }
            consumed = MARKER_LENGTH + integerConsumed;
            return true;
        }

        public bool TryGetBytes(double value, Span<byte> buffer, out int consumed)
        {
            consumed = default;
            if (buffer.Length < MARKER_LENGTH + sizeof(double))
            {
                return false;
            }

            buffer[0] = (byte)Amf3Type.Double;
            if (!RtmpBitConverter.TryGetBytes(value, buffer.Slice(MARKER_LENGTH)))
            {
                return false;
            }
            consumed = MARKER_LENGTH + sizeof(double);
            return true;

        }

        private bool TryGetStringBytesImpl<T>(string value, List<T> referenceTable, Span<byte> buffer, out int consumed)
        {
            consumed = default;
            if (value is T tValue)
            {
                var refIndex = referenceTable.IndexOf(tValue);
                if (refIndex >= 0)
                {
                    var header = (uint)refIndex << 1;
                    if (!TryGetU29BytesImpl(header, buffer, out var headerConsumed))
                    {
                        return false;
                    }
                    consumed = headerConsumed;
                    return true;
                }
                else
                {
                    var byteCount = (uint)Encoding.UTF8.GetByteCount(value);
                    var header = (byteCount << 1) | 0x01;
                    if (!TryGetU29BytesImpl(header, buffer, out var headerConsumed))
                    {
                        return false;
                    }
                    if (buffer.Length - headerConsumed < byteCount)
                    {
                        return false;
                    }
                    Encoding.UTF8.GetBytes(value, buffer.Slice(headerConsumed));
                    consumed = (int)(headerConsumed + byteCount);
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

        public bool TryGetBytes(string value, Span<byte> buffer, out int consumed)
        {
            consumed = default;

            if (buffer.Length < MARKER_LENGTH)
            {
                return false;
            }

            buffer[0] = (byte)Amf3Type.String;
            if (!TryGetStringBytesImpl(value, _writeStringReferenceTable, buffer.Slice(MARKER_LENGTH), out var strConsumed))
            {
                return false;
            }
            consumed = MARKER_LENGTH + strConsumed;

            return true;
        }

        public bool TryGetBytes(XmlDocument xml, Span<byte> buffer, out int consumed)
        {
            consumed = default;
            if (buffer.Length < MARKER_LENGTH)
            {
                return false;
            }

            buffer[0] = (byte)Amf3Type.XmlDocument;
            var content = XmlToString(xml);

            if (!TryGetStringBytesImpl(content, _writeObjectReferenceTable, buffer.Slice(MARKER_LENGTH), out var xmlConsumed))
            {
                return false;
            }

            consumed = MARKER_LENGTH + xmlConsumed;
            return true;
        }

        public bool TryGetBytes(DateTime dateTime, Span<byte> buffer, out int consumed)
        {
            consumed = default;

            if (buffer.Length < MARKER_LENGTH)
            {
                return false;
            }

            buffer[0] = (byte)Amf3Type.Date;

            var refIndex = _writeObjectReferenceTable.IndexOf(dateTime);
            uint header = 0;
            int headerConsumed = 0;
            if (refIndex >= 0)
            {
                header = (uint)refIndex << 1;

                if (!TryGetU29BytesImpl(header, buffer.Slice(MARKER_LENGTH), out headerConsumed))
                {
                    return false;
                }

                consumed = MARKER_LENGTH + headerConsumed;
                return true;
            }

            var timeOffset = new DateTimeOffset(dateTime);
            var timestamp = timeOffset.ToUnixTimeMilliseconds() / 1000.0d;
            header = 0x01;
            if (!TryGetU29BytesImpl(header, buffer.Slice(MARKER_LENGTH), out headerConsumed))
            {
                return false;
            }

            if (!RtmpBitConverter.TryGetBytes(timestamp, buffer.Slice(MARKER_LENGTH + headerConsumed)))
            {
                return false;
            }

            _writeObjectReferenceTable.Add(dateTime);
            return true;
        }

        public bool TryGetBytes(Amf3Xml xml, Span<byte> buffer, out int consumed)
        {
            consumed = default;
            if (buffer.Length < MARKER_LENGTH)
            {
                return false;
            }

            buffer[0] = (byte)Amf3Type.Xml;
            var content = XmlToString(xml);

            if (!TryGetStringBytesImpl(content, _writeObjectReferenceTable, buffer.Slice(MARKER_LENGTH), out var xmlConsumed))
            {
                return false;
            }

            consumed = MARKER_LENGTH + xmlConsumed;
            return true;
        }

        public bool TryGetBytes(byte[] value, Span<byte> buffer, out int consumed)
        {
            consumed = default;
            if (buffer.Length < MARKER_LENGTH)
            {
                return false;
            }

            buffer[0] = (byte)Amf3Type.ByteArray;
            uint header = 0;
            int headerConsumed = 0;
            var refIndex = _writeObjectReferenceTable.FindIndex(o => o is byte[] b ? b.SequenceEqual(value) : false);
            if (refIndex >= 0)
            {
                header = (uint)refIndex << 1;
                if (!TryGetU29BytesImpl(header, buffer.Slice(MARKER_LENGTH), out headerConsumed))
                {
                    return false;
                }

                consumed = MARKER_LENGTH + headerConsumed;
                return true;
            }

            header = ((uint)value.Length << 1) | 0x01;
            if (!TryGetU29BytesImpl(header, buffer.Slice(MARKER_LENGTH), out headerConsumed))
            {
                return false;
            }

            if (buffer.Length - MARKER_LENGTH - headerConsumed < value.Length)
            {
                return false;
            }

            value.CopyTo(buffer.Slice(MARKER_LENGTH + headerConsumed));
            consumed = MARKER_LENGTH + headerConsumed + value.Length;
            _writeObjectReferenceTable.Add(value);
            return true;
        }

        public bool TryGetValueBytes(object value, Span<byte> buffer, out int consumed)
        {
            consumed = default;
            var valueType = value.GetType();

            if (!_writeHandlers.TryGetValue(valueType, out var handler))
            {
                return false;
            }

            if (!handler(value, buffer, out consumed))
            {
                return false;
            }
            return true;
        }

        public bool TryGetBytes(object value, Span<byte> buffer, out int consumed)
        {
            consumed = 0;
            uint header = 0;
            if (buffer.Length < MARKER_LENGTH)
            {
                return false;
            }
            if (value == null)
            {
                buffer[0] = (byte)Amf3Type.Null;
                consumed = MARKER_LENGTH;
                return true;
            }
            else
            {
                buffer[0] = (byte)Amf3Type.Object;
                buffer = buffer.Slice(MARKER_LENGTH);
                consumed = MARKER_LENGTH;
            }

            var refIndex = _writeObjectReferenceTable.IndexOf(value);
            if (refIndex >= 0)
            {
                header = (uint)refIndex << 1;
                if (!TryGetU29BytesImpl(header, buffer, out var headerLength))
                {
                    return false;
                }
                consumed += headerLength;
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

            var traitRefIndex = _writeObjectTraitsReferenceTable.IndexOf(traits);
            if (traitRefIndex >= 0)
            {
                header = ((uint)traitRefIndex << 2) | 0x03;
                if (!TryGetU29BytesImpl(header, buffer, out var headerConsumed))
                {
                    return false;
                }
                buffer = buffer.Slice(headerConsumed);
                consumed += headerConsumed;
            }
            else
            {
                if (traits.ClassType == Amf3ClassType.Externalizable)
                {
                    header = 0x07;
                    if (!TryGetU29BytesImpl(header, buffer, out var headerConsumed))
                    {
                        return false;
                    }
                    buffer = buffer.Slice(headerConsumed);
                    consumed += headerConsumed;
                    if (!TryGetStringBytesImpl(traits.ClassName, _writeStringReferenceTable, buffer, out var classNameConsumed))
                    {
                        return false;
                    }
                    buffer = buffer.Slice(classNameConsumed);
                    consumed += classNameConsumed;
                    var extObj = value as IExternalizable;
                    if (!extObj.TryEncodeData(buffer, out var bodyConsumed))
                    {
                        return false;
                    }
                    consumed += bodyConsumed;
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
                    if (!TryGetU29BytesImpl(header, buffer, out var traitsHeaderConsumed))
                    {
                        return false;
                    }

                    buffer = buffer.Slice(traitsHeaderConsumed);
                    consumed += traitsHeaderConsumed;
                    foreach (var memberName in traits.Members)
                    {
                        if (!TryGetStringBytesImpl(memberName, _writeStringReferenceTable, buffer, out var memberConsumed))
                        {
                            return false;
                        }
                        buffer = buffer.Slice(memberConsumed);
                        consumed += memberConsumed;
                    }
                }
                _writeObjectTraitsReferenceTable.Add(traits);
            }

            foreach (var memberValue in memberValues)
            {
                if (!TryGetValueBytes(memberValue, buffer, out var memberConsumed))
                {
                    return false;
                }
                buffer = buffer.Slice(memberConsumed);
                consumed += memberConsumed;
            }

            if (traits.ClassType == Amf3ClassType.Dynamic)
            {
                var amf3Obj = value as Amf3Object;
                foreach ((var key, var item) in amf3Obj.DynamicFields)
                {
                    if (!TryGetStringBytesImpl(key, _writeStringReferenceTable, buffer, out var keyConsumed))
                    {
                        return false;
                    }
                    buffer = buffer.Slice(keyConsumed);
                    consumed += keyConsumed;
                    if (!TryGetValueBytes(item, buffer, out var itemConsumed))
                    {
                        return false;
                    }
                    buffer = buffer.Slice(itemConsumed);
                    consumed += itemConsumed;
                }
            }
            _writeObjectReferenceTable.Add(value);
            return true;
        }

        public bool TryGetBytes(Vector<uint> value, Span<byte> buffer, out int consumed)
        {
            consumed = 0;
            if (buffer.Length < MARKER_LENGTH)
            {
                return false;
            }
            buffer[0] = (byte)Amf3Type.VectorDouble;
            buffer = buffer.Slice(MARKER_LENGTH);
            consumed += MARKER_LENGTH;

            var refIndex = _writeObjectReferenceTable.IndexOf(value);
            if (refIndex >= 0)
            {
                var header = (uint)refIndex << 1;
                if (!TryGetU29BytesImpl(header, buffer, out var headerLength))
                {
                    return false;
                }
                consumed += headerLength;
                return true;
            }
            else
            {
                var header = ((uint)value.Count << 1) | 0x01;
                if (!TryGetU29BytesImpl(header, buffer, out var headerLength))
                {
                    return false;
                }
                consumed += headerLength;
                buffer = buffer.Slice(headerLength);
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
                _writeObjectReferenceTable.Add(value);
                return true;
            }
        }

        public bool TryGetBytes(Vector<int> value, Span<byte> buffer, out int consumed)
        {
            consumed = 0;
            if (buffer.Length < MARKER_LENGTH)
            {
                return false;
            }
            buffer[0] = (byte)Amf3Type.VectorDouble;
            buffer = buffer.Slice(MARKER_LENGTH);
            consumed += MARKER_LENGTH;

            var refIndex = _writeObjectReferenceTable.IndexOf(value);
            if (refIndex >= 0)
            {
                var header = (uint)refIndex << 1;
                if (!TryGetU29BytesImpl(header, buffer, out var headerLength))
                {
                    return false;
                }
                consumed += headerLength;
                return true;
            }
            else
            {
                var header = ((uint)value.Count << 1) | 0x01;
                if (!TryGetU29BytesImpl(header, buffer, out var headerLength))
                {
                    return false;
                }
                consumed += headerLength;
                buffer = buffer.Slice(headerLength);
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
                _writeObjectReferenceTable.Add(value);
                return true;
            }
        }

        public bool TryGetBytes(Vector<double> value, Span<byte> buffer, out int consumed)
        {
            consumed = 0;
            if (buffer.Length < MARKER_LENGTH)
            {
                return false;
            }
            buffer[0] = (byte)Amf3Type.VectorDouble;
            buffer = buffer.Slice(MARKER_LENGTH);
            consumed += MARKER_LENGTH;

            var refIndex = _writeObjectReferenceTable.IndexOf(value);
            if (refIndex >= 0)
            {
                var header = (uint)refIndex << 1;
                if (!TryGetU29BytesImpl(header, buffer, out var headerLength))
                {
                    return false;
                }
                consumed += headerLength;
                return true;
            }
            else
            {
                var header = ((uint)value.Count << 1) | 0x01;
                if (!TryGetU29BytesImpl(header, buffer, out var headerLength))
                {
                    return false;
                }
                consumed += headerLength;
                buffer = buffer.Slice(headerLength);
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
                _writeObjectReferenceTable.Add(value);
                return true;
            }
        }

        public bool TryGetBytes<T>(Vector<T> value, Span<byte> buffer, out int consumed)
        {
            consumed = 0;
            if (buffer.Length < MARKER_LENGTH)
            {
                return false;
            }
            buffer[0] = (byte)Amf3Type.VectorDouble;
            buffer = buffer.Slice(MARKER_LENGTH);
            consumed += MARKER_LENGTH;

            var refIndex = _writeObjectReferenceTable.IndexOf(value);
            if (refIndex >= 0)
            {
                var header = (uint)refIndex << 1;
                if (!TryGetU29BytesImpl(header, buffer, out var headerLength))
                {
                    return false;
                }
                consumed += headerLength;
                return true;
            }
            else
            {
                var header = ((uint)value.Count << 1) | 0x01;
                if (!TryGetU29BytesImpl(header, buffer, out var headerLength))
                {
                    return false;
                }
                consumed += headerLength;
                buffer = buffer.Slice(headerLength);
                if (!RtmpBitConverter.TryGetBytes(value.IsFixedSize ? 0x01 : 0x00, buffer))
                {
                    return false;
                }
                buffer = buffer.Slice(sizeof(byte));
                consumed += sizeof(byte);

                var className = typeof(T) == typeof(object) ? "*" : typeof(T).Name;
                if (!TryGetStringBytesImpl(className, _writeStringReferenceTable, buffer, out var classNameConsumed))
                {
                    return false;
                }
                buffer = buffer.Slice(classNameConsumed);
                consumed += classNameConsumed;

                foreach (var i in value)
                {
                    if (!TryGetValueBytes((object)i, buffer, out var itemConsumed))
                    {
                        return false;
                    }
                    buffer = buffer.Slice(itemConsumed);
                    consumed += itemConsumed;
                }
                _writeObjectReferenceTable.Add(value);
                return true;
            }

        }

        public bool TryGetBytes(Dictionary<string, object> value, Span<byte> buffer, out int consumed)
        {
            consumed = 0;
            if (buffer.Length < MARKER_LENGTH)
            {
                return false;
            }
            buffer[0] = (byte)Amf3Type.Array;

            buffer = buffer.Slice(MARKER_LENGTH);

            var refIndex = _writeObjectReferenceTable.IndexOf(value);
            if (refIndex >= 0)
            {
                var header = (uint)refIndex << 1;
                if (!TryGetU29BytesImpl(header, buffer, out var headerLength))
                {
                    return false;
                }
                consumed += headerLength;
                return true;
            }
            else
            {
                var header = ((uint)value.Count << 1) & 0x01;
                if (!TryGetU29BytesImpl(header, buffer, out var headerLength))
                {
                    return false;
                }
                consumed += headerLength;
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
                    if (!TryGetStringBytesImpl("", _writeStringReferenceTable, buffer, out var keyConsumed))
                    {
                        return false;
                    }
                    consumed += keyConsumed;
                    buffer = buffer.Slice(keyConsumed);
                    foreach (var i in value.Keys)
                    {
                        if (!TryGetValueBytes(value[i], buffer, out var itemConsumed))
                        {
                            return false;
                        }
                        buffer = buffer.Slice(itemConsumed);
                        consumed += itemConsumed;
                    }
                }
                else
                {
                    foreach ((var key, var item) in value)
                    {
                        if (!TryGetStringBytesImpl(key, _writeStringReferenceTable, buffer, out var keyConsumed))
                        {
                            return false;
                        }
                        buffer = buffer.Slice(keyConsumed);
                        consumed += keyConsumed;

                        if (!TryGetValueBytes(item, buffer, out var itemConsumed))
                        {
                            return false;
                        }
                        buffer = buffer.Slice(itemConsumed);
                        consumed += itemConsumed;
                    }
                }
                _writeObjectReferenceTable.Add(value);
                return true;
            }
        }

        public bool TryGetBytes<TKey, TValue>(Amf3Dictionary<TKey, TValue> value, Span<byte> buffer, out int consumed)
        {
            consumed = 0;

            if (buffer.Length < MARKER_LENGTH)
            {
                return false;
            }

            buffer[0] = (byte)Amf3Type.Dictionary;
            buffer = buffer.Slice(MARKER_LENGTH);

            var refIndex = _writeObjectReferenceTable.IndexOf(value);

            if (refIndex >= 0)
            {
                var header = (uint)refIndex << 1;
                if (!TryGetU29BytesImpl(header, buffer, out var headerLength))
                {
                    return false;
                }
                buffer = buffer.Slice(headerLength);
                consumed += headerLength;
                return true;
            }
            else
            {
                var header = (uint)value.Count << 1;
                if (!TryGetU29BytesImpl(header, buffer, out var headerLength))
                {
                    return false;
                }
                buffer = buffer.Slice(headerLength);
                consumed += headerLength;
                if (buffer.Length < sizeof(byte))
                {
                    return false;
                }
                buffer[0] = (byte)(value.WeakKeys ? 0x01 : 0x00);
                buffer = buffer.Slice(sizeof(byte));
                consumed += sizeof(byte);
                foreach ((var key, var item) in value)
                {
                    if (!TryGetValueBytes(key, buffer, out var keyConsumed))
                    {
                        return false;
                    }
                    buffer = buffer.Slice(keyConsumed);
                    consumed += keyConsumed;
                    if (!TryGetValueBytes(item, buffer, out var itemConsumed))
                    {
                        return false;
                    }
                    buffer = buffer.Slice(itemConsumed);
                    consumed += itemConsumed;
                }
                _writeObjectReferenceTable.Add(value);
                return true;
            }
        }

    }
}
