using Harmonic.Networking.Amf.Common;
using System;
using System.Linq;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.IO;
using Harmonic.Networking.Utils;
using Harmonic.Networking.Amf.Data;
using System.Diagnostics.Contracts;
using System.Reflection;
using Harmonic.Networking.Amf.Attributes;
using Harmonic.Networking.Amf.Serialization.Attributes;

namespace Harmonic.Networking.Amf.Serialization.Amf3;

public class Amf3Writer
{
    private delegate void WriteHandler<T>(T value, SerializationContext context);
    private delegate void WriteHandler(object value, SerializationContext context);

    private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;

    private readonly Dictionary<Type, WriteHandler> _writeHandlers = new();

    public static readonly uint U29MAX = 0x1FFFFFFF;
    private readonly MethodInfo _writeVectorTMethod = null;
    private readonly MethodInfo _writeDictionaryTMethod = null;

    public Amf3Writer()
    {

        var writeHandlers = new Dictionary<Type, WriteHandler>
        {
            [typeof(int)] = WriteHandlerWrapper<double>(WriteBytes),
            [typeof(uint)] = WriteHandlerWrapper<uint>(WriteBytes),
            [typeof(long)] = WriteHandlerWrapper<double>(WriteBytes),
            [typeof(ulong)] = WriteHandlerWrapper<uint>(WriteBytes),
            [typeof(short)] = WriteHandlerWrapper<double>(WriteBytes),
            [typeof(ushort)] = WriteHandlerWrapper<uint>(WriteBytes),
            [typeof(double)] = WriteHandlerWrapper<double>(WriteBytes),
            [typeof(Undefined)] = WriteHandlerWrapper<Undefined>(WriteBytes),
            [typeof(object)] = WriteHandlerWrapper<object>(WriteBytes),
            [typeof(DateTime)] = WriteHandlerWrapper<DateTime>(WriteBytes),
            [typeof(XmlDocument)] = WriteHandlerWrapper<XmlDocument>(WriteBytes),
            [typeof(Amf3Xml)] = WriteHandlerWrapper<Amf3Xml>(WriteBytes),
            [typeof(bool)] = WriteHandlerWrapper<bool>(WriteBytes),
            [typeof(Memory<byte>)] = WriteHandlerWrapper<Memory<byte>>(WriteBytes),
            [typeof(string)] = WriteHandlerWrapper<string>(WriteBytes),
            [typeof(Vector<int>)] = WriteHandlerWrapper<Vector<int>>(WriteBytes),
            [typeof(Vector<uint>)] = WriteHandlerWrapper<Vector<uint>>(WriteBytes),
            [typeof(Vector<double>)] = WriteHandlerWrapper<Vector<double>>(WriteBytes),
            [typeof(Vector<>)] = WrapVector,
            [typeof(Amf3Dictionary<,>)] = WrapDictionary
        };
        _writeHandlers = writeHandlers;

        Action<Vector<int>, SerializationContext> method = WriteBytes<int>;
        _writeVectorTMethod = method.Method.GetGenericMethodDefinition();

        Action<Amf3Dictionary<int, int>, SerializationContext> dictMethod = WriteBytes;
        _writeDictionaryTMethod = dictMethod.Method.GetGenericMethodDefinition();

    }

    private void WrapVector(object value, SerializationContext context)
    {
        var valueType = value.GetType();
        var contractRet = valueType.IsGenericType;
        Contract.Assert(contractRet);
        var defination = valueType.GetGenericTypeDefinition();
        Contract.Assert(defination == typeof(Vector<>));
        var vectorT = valueType.GetGenericArguments().First();

        _writeVectorTMethod.MakeGenericMethod(vectorT).Invoke(this, new object[] { value, context });
    }

    private void WrapDictionary(object value, SerializationContext context)
    {
        var valueType = value.GetType();
        var contractRet = valueType.IsGenericType;
        Contract.Assert(contractRet);
        var defination = valueType.GetGenericTypeDefinition();
        Contract.Assert(defination == typeof(Amf3Dictionary<,>));
        var tKey = valueType.GetGenericArguments().First();
        var tValue = valueType.GetGenericArguments().Last();

        _writeDictionaryTMethod.MakeGenericMethod(tKey, tValue).Invoke(this, new object[] { value, context });
    }

    private WriteHandler WriteHandlerWrapper<T>(WriteHandler<T> handler)
    {
        return (object obj, SerializationContext context) =>
        {
            if (obj is T tObj)
            {
                handler(tObj, context);
            }
            else
            {
                handler((T)Convert.ChangeType(obj, typeof(T)), context);
            }

        };
    }

    private string XmlToString(XmlDocument xml)
    {
        using var stringWriter = new StringWriter();
        using var xmlTextWriter = XmlWriter.Create(stringWriter);
        xml.WriteTo(xmlTextWriter);
        xmlTextWriter.Flush();
        return stringWriter.GetStringBuilder().ToString();
    }

    public void WriteBytes(Undefined value, SerializationContext context)
    {
        context.Buffer.WriteToBuffer((byte)Amf3Type.Undefined);
    }

    public void WriteBytes(bool value, SerializationContext context)
    {
        if (value)
        {
            context.Buffer.WriteToBuffer((byte)Amf3Type.True);
        }
        else
        {
            context.Buffer.WriteToBuffer((byte)Amf3Type.False);
        }

    }

    private void WriteU29BytesImpl(uint value, SerializationContext context)
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
                    context.Buffer.WriteToBuffer((byte)(arr[0] << 2 | ((arr[1]) >> 6) | 0x80));
                    context.Buffer.WriteToBuffer((byte)(arr[1] << 1 | ((arr[2]) >> 7) | 0x80));
                    context.Buffer.WriteToBuffer((byte)(arr[2] | 0x80));
                    context.Buffer.WriteToBuffer(arr[3]);
                    break;
                case 3:
                    context.Buffer.WriteToBuffer((byte)(arr[1] << 2 | ((arr[2]) >> 6) | 0x80));
                    context.Buffer.WriteToBuffer((byte)(arr[2] << 1 | ((arr[3]) >> 7) | 0x80));
                    context.Buffer.WriteToBuffer((byte)(arr[3] & 0x7F));
                    break;
                case 2:
                    context.Buffer.WriteToBuffer((byte)(arr[2] << 1 | ((arr[3]) >> 7) | 0x80));
                    context.Buffer.WriteToBuffer((byte)(arr[3] & 0x7F));
                    break;
                case 1:
                    context.Buffer.WriteToBuffer((byte)(arr[3]));
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

    public void WriteBytes(uint value, SerializationContext context)
    {
        context.Buffer.WriteToBuffer((byte)Amf3Type.Integer);
        WriteU29BytesImpl(value, context);
    }

    public void WriteBytes(double value, SerializationContext context)
    {
        context.Buffer.WriteToBuffer((byte)Amf3Type.Double);
        var backend = _arrayPool.Rent(sizeof(double));
        try
        {
            var contractRet = NetworkBitConverter.TryGetBytes(value, backend);
            Contract.Assert(contractRet);
            context.Buffer.WriteToBuffer(backend.AsSpan(0, sizeof(double)));
        }
        finally
        {
            _arrayPool.Return(backend);
        }

    }

    private void WriteStringBytesImpl<T>(string value, SerializationContext context, List<T> referenceTable)
    {
        if (value is T tValue)
        {
            var refIndex = referenceTable.IndexOf(tValue);
            if (refIndex >= 0)
            {
                var header = (uint)refIndex << 1;
                WriteU29BytesImpl(header, context);
                return;
            }
            else
            {
                var byteCount = (uint)Encoding.UTF8.GetByteCount(value);
                var header = (byteCount << 1) | 0x01;
                WriteU29BytesImpl(header, context);
                var backend = _arrayPool.Rent((int)byteCount);
                try
                {
                    Encoding.UTF8.GetBytes(value, backend);
                    context.Buffer.WriteToBuffer(backend.AsSpan(0, (int)byteCount));
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

    public void WriteBytes(string value, SerializationContext context)
    {
        context.Buffer.WriteToBuffer((byte)Amf3Type.String);
        WriteStringBytesImpl(value, context, context.StringReferenceTable);
    }

    public void WriteBytes(XmlDocument xml, SerializationContext context)
    {
        context.Buffer.WriteToBuffer((byte)Amf3Type.XmlDocument);
        var content = XmlToString(xml);

        WriteStringBytesImpl(content, context, context.ObjectReferenceTable);
    }

    public void WriteBytes(DateTime dateTime, SerializationContext context)
    {
        context.Buffer.WriteToBuffer((byte)Amf3Type.Date);

        var refIndex = context.ObjectReferenceTable.IndexOf(dateTime);
        uint header = 0;
        if (refIndex >= 0)
        {
            header = (uint)refIndex << 1;

            WriteU29BytesImpl(header, context);
            return;
        }
        context.ObjectReferenceTable.Add(dateTime);

        var timeOffset = new DateTimeOffset(dateTime);
        var timestamp = timeOffset.ToUnixTimeMilliseconds();
        header = 0x01;
        WriteU29BytesImpl(header, context);
        var backend = _arrayPool.Rent(sizeof(double));
        try
        {
            var contractRet = NetworkBitConverter.TryGetBytes(timestamp, backend);
            Contract.Assert(contractRet);
            context.Buffer.WriteToBuffer(backend.AsSpan(0, sizeof(double)));
        }
        finally
        {
            _arrayPool.Return(backend);
        }

    }

    public void WriteBytes(Amf3Xml xml, SerializationContext context)
    {
        context.Buffer.WriteToBuffer((byte)Amf3Type.Xml);
        var content = XmlToString(xml);

        WriteStringBytesImpl(content, context, context.ObjectReferenceTable);
    }

    public void WriteBytes(Memory<byte> value, SerializationContext context)
    {
        context.Buffer.WriteToBuffer((byte)Amf3Type.ByteArray);
        uint header = 0;
        var refIndex = context.ObjectReferenceTable.IndexOf(value);
        if (refIndex >= 0)
        {
            header = (uint)refIndex << 1;
            WriteU29BytesImpl(header, context);
            return;
        }

        header = ((uint)value.Length << 1) | 0x01;
        WriteU29BytesImpl(header, context);

        context.Buffer.WriteToBuffer(value.Span);
        context.ObjectReferenceTable.Add(value);
    }

    public void WriteValueBytes(object value, SerializationContext context)
    {
        if (value == null)
        {
            WriteBytes((object)null, context);
            return;
        }
        var valueType = value.GetType();
        if (_writeHandlers.TryGetValue(valueType, out var handler))
        {
            handler(value, context);
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
                    handler(value, context);
                }
            }
            else
            {
                WriteBytes(value, context);
            }
        }


    }

    public void WriteBytes(object value, SerializationContext context)
    {
        uint header = 0;
        if (value == null)
        {
            context.Buffer.WriteToBuffer((byte)Amf3Type.Null);
            return;
        }
        else
        {
            context.Buffer.WriteToBuffer((byte)Amf3Type.Object);
        }

        var refIndex = context.ObjectReferenceTable.IndexOf(value);
        if (refIndex >= 0)
        {
            header = (uint)refIndex << 1;
            WriteU29BytesImpl(header, context);
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
        if (value is AmfObject amf3Object)
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
        context.ObjectReferenceTable.Add(value);


        var traitRefIndex = context.ObjectTraitsReferenceTable.IndexOf(traits);
        if (traitRefIndex >= 0)
        {
            header = ((uint)traitRefIndex << 2) | 0x01;
            WriteU29BytesImpl(header, context);
        }
        else
        {
            if (traits.ClassType == Amf3ClassType.Externalizable)
            {
                header = 0x07;
                WriteU29BytesImpl(header, context);
                WriteStringBytesImpl(traits.ClassName, context, context.StringReferenceTable);
                var extObj = value as IExternalizable;
                extObj.TryEncodeData(context.Buffer);
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
                WriteU29BytesImpl(header, context);
                WriteStringBytesImpl(traits.ClassName, context, context.StringReferenceTable);

                foreach (var memberName in traits.Members)
                {
                    WriteStringBytesImpl(memberName, context, context.StringReferenceTable);
                }
            }
            context.ObjectTraitsReferenceTable.Add(traits);
        }


        foreach (var memberValue in memberValues)
        {
            WriteValueBytes(memberValue, context);
        }

        if (traits.IsDynamic)
        {
            var amf3Obj = value as IDynamicObject;
            foreach ((var key, var item) in amf3Obj.DynamicFields)
            {
                WriteStringBytesImpl(key, context, context.StringReferenceTable);
                WriteValueBytes(item, context);
            }
            WriteStringBytesImpl("", context, context.StringReferenceTable);
        }
    }

    public void WriteBytes(Vector<uint> value, SerializationContext context)
    {
        context.Buffer.WriteToBuffer((byte)Amf3Type.VectorUInt);

        var refIndex = context.ObjectReferenceTable.IndexOf(value);
        if (refIndex >= 0)
        {
            var header = (uint)refIndex << 1;
            WriteU29BytesImpl(header, context);
            return;
        }
        else
        {
            context.ObjectReferenceTable.Add(value);
            var header = ((uint)value.Count << 1) | 0x01;
            WriteU29BytesImpl(header, context);
            context.Buffer.WriteToBuffer(value.IsFixedSize ? (byte)0x01 : (byte)0x00);
            var buffer = _arrayPool.Rent(sizeof(uint));
            try
            {
                foreach (var i in value)
                {
                    var contractRet = NetworkBitConverter.TryGetBytes(i, buffer);
                    Contract.Assert(contractRet);
                    context.Buffer.WriteToBuffer(buffer.AsSpan(0, sizeof(uint)));
                }
            }
            finally
            {
                _arrayPool.Return(buffer);
            }
        }
    }

    public void WriteBytes(Vector<int> value, SerializationContext context)
    {
        context.Buffer.WriteToBuffer((byte)Amf3Type.VectorInt);

        var refIndex = context.ObjectReferenceTable.IndexOf(value);
        if (refIndex >= 0)
        {
            var header = (uint)refIndex << 1;
            WriteU29BytesImpl(header, context);
            return;
        }
        else
        {
            context.ObjectReferenceTable.Add(value);
            var header = ((uint)value.Count << 1) | 0x01;
            WriteU29BytesImpl(header, context);
            context.Buffer.WriteToBuffer(value.IsFixedSize ? (byte)0x01 : (byte)0x00);
            var buffer = _arrayPool.Rent(sizeof(int));
            try
            {

                foreach (var i in value)
                {
                    var contractRet = NetworkBitConverter.TryGetBytes(i, buffer);
                    Contract.Assert(contractRet);
                    context.Buffer.WriteToBuffer(buffer.AsSpan(0, sizeof(int)));
                }
            }
            finally
            {
                _arrayPool.Return(buffer);
            }
            return;
        }
    }

    public void WriteBytes(Vector<double> value, SerializationContext context)
    {
        context.Buffer.WriteToBuffer((byte)Amf3Type.VectorDouble);

        var refIndex = context.ObjectReferenceTable.IndexOf(value);
        if (refIndex >= 0)
        {
            var header = (uint)refIndex << 1;
            WriteU29BytesImpl(header, context);
            return;
        }
        else
        {
            context.ObjectReferenceTable.Add(value);
            var header = ((uint)value.Count << 1) | 0x01;
            WriteU29BytesImpl(header, context);
            context.Buffer.WriteToBuffer(value.IsFixedSize ? (byte)0x01 : (byte)0x00);
            var buffer = _arrayPool.Rent(sizeof(double));
            try
            {
                foreach (var i in value)
                {
                    var contractRet = NetworkBitConverter.TryGetBytes(i, buffer);
                    Contract.Assert(contractRet);
                    context.Buffer.WriteToBuffer(buffer.AsSpan(0, sizeof(double)));
                }
            }
            finally
            {
                _arrayPool.Return(buffer);
            }
            return;
        }
    }

    public void WriteBytes<T>(Vector<T> value, SerializationContext context)
    {
        context.Buffer.WriteToBuffer((byte)Amf3Type.VectorObject);

        var refIndex = context.ObjectReferenceTable.IndexOf(value);
        if (refIndex >= 0)
        {
            var header = (uint)refIndex << 1;
            WriteU29BytesImpl(header, context);
            return;
        }
        else
        {
            context.ObjectReferenceTable.Add(value);
            var header = ((uint)value.Count << 1) | 0x01;
            WriteU29BytesImpl(header, context);
            context.Buffer.WriteToBuffer(value.IsFixedSize ? (byte)0x01 : (byte)0x00);
            var tType = typeof(T);

            string typeName = tType.Name;
            var attr = tType.GetCustomAttribute<TypedObjectAttribute>();
            if (attr != null)
            {
                typeName = attr.Name;
            }

            var className = typeof(T) == typeof(object) ? "*" : typeName;
            WriteStringBytesImpl(className, context, context.StringReferenceTable);

            foreach (var i in value)
            {
                WriteValueBytes(i, context);
            }
            return;
        }

    }

    public void WriteBytes(Amf3Array value, SerializationContext context)
    {
        context.Buffer.WriteToBuffer((byte)Amf3Type.Array);

        var refIndex = context.ObjectReferenceTable.IndexOf(value);
        if (refIndex >= 0)
        {
            var header = (uint)refIndex << 1;
            WriteU29BytesImpl(header, context);
            return;
        }
        else
        {
            context.ObjectReferenceTable.Add(value);
            var header = ((uint)value.DensePart.Count << 1) | 0x01;
            WriteU29BytesImpl(header, context);
            foreach ((var key, var item) in value.SparsePart)
            {
                WriteStringBytesImpl(key, context, context.StringReferenceTable);
                WriteValueBytes(item, context);
            }
            WriteStringBytesImpl("", context, context.StringReferenceTable);

            foreach (var i in value.DensePart)
            {
                WriteValueBytes(i, context);
            }

            return;
        }
    }

    public void WriteBytes<TKey, TValue>(Amf3Dictionary<TKey, TValue> value, SerializationContext context)
    {
        context.Buffer.WriteToBuffer((byte)Amf3Type.Dictionary);

        var refIndex = context.ObjectReferenceTable.IndexOf(value);

        if (refIndex >= 0)
        {
            var header = (uint)refIndex << 1;
            WriteU29BytesImpl(header, context);
            return;
        }
        else
        {
            context.ObjectReferenceTable.Add(value);
            var header = (uint)value.Count << 1 | 0x01;
            WriteU29BytesImpl(header, context);

            context.Buffer.WriteToBuffer((byte)(value.WeakKeys ? 0x01 : 0x00));
            foreach ((var key, var item) in value)
            {
                WriteValueBytes(key, context);
                WriteValueBytes(item, context);
            }
            return;
        }
    }

}