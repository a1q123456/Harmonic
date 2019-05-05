using RtmpSharp.IO.AMF0;
using RtmpSharp.IO.AMF3;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace RtmpSharp.IO
{
    public class AmfReader : IDisposable
    {
        public SerializationContext SerializationContext { get; private set; }
        public readonly BinaryReader underlying;
        readonly List<object> amf0ObjectReferences;
        readonly List<object> amf3ObjectReferences;
        readonly List<object> stringReferences;
        readonly List<Amf3ClassDescription> amf3ClassDefinitions;
        public bool asyncMode { get; private set; }

        static DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        struct Amf3ClassDescription
        {
            public string TypeName;
            public string[] MemberNames;
            public bool IsExternalizable;
            public bool IsDynamic;
            public bool IsTyped => !string.IsNullOrEmpty(TypeName);
        }

        static readonly List<Func<AmfReader, CancellationToken, Task<object>>> Amf0TypeAsyncReaders = new List<Func<AmfReader, CancellationToken, Task<object>>>
        {
            async (r, ct) => await r.ReadDoubleAsync(ct),                                          // 0x00 - number
            async (r, ct) => await r.ReadBooleanAsync(ct),                                         // 0x01 - boolean
            async (r, ct) => await r.ReadUtfAsync(ct),                                             // 0x02 - string
            async (r, ct) => await r.ReadAmf0AsObjectAsync(ct),                                    // 0x03 - object
            (r, ct) => { throw new NotSupportedException(); },                  // 0x04 - movieclip
            (r, ct) => null,                                                    // 0x05 - null
            (r, ct) => null,                                                    // 0x06 - undefined
            async (r, ct) => await r.ReadAmf0ObjectReferenceAsync(ct),                             // 0x07 - reference
            async (r, ct) => await r.ReadAmf0AssociativeArrayAsync(ct),                            // 0x08 - ECMA array
            (r, ct) => { throw new NotSupportedException(); },                  // 0x09 - 'object end marker' - we handle this in deserializer block; we shouldn't encounter it here
            async (r, ct) => await r.ReadAmf0ArrayAsync(ct),                                       // 0x0A - strict array
            async (r, ct) => await r.ReadAmf0DateAsync(ct),                                        // 0x0B - date
            async (r, ct) => await r.ReadAmf0LongStringAsync(ct),                                  // 0x0C - long string
            (r, ct) => { throw new NotSupportedException(); },                  // 0x0D - 'unsupported marker'
            (r, ct) => { throw new NotSupportedException(); },                  // 0x0E - recordset
            async (r, ct) => await r.ReadAmf0XmlDocumentAsync(ct),                                 // 0x0F - xml document
            async (r, ct) => await r.ReadAmf0ObjectAsync(ct),                                      // 0x10 - typed object
            async (r, ct) => await r.ReadAmf3ItemAsync(ct)                                         // 0x11 - avmplus object
        };

        static readonly List<Func<AmfReader, object>> Amf0TypeReaders = new List<Func<AmfReader, object>>
        {
            r => r.ReadDouble(),                                          // 0x00 - number
            r => r.ReadBoolean(),                                         // 0x01 - boolean
            r => r.ReadUtf(),                                             // 0x02 - string
            r => r.ReadAmf0AsObject(),                                    // 0x03 - object
            r => { throw new NotSupportedException(); },                  // 0x04 - movieclip
            r => null,                                                    // 0x05 - null
            r => null,                                                    // 0x06 - undefined
            r => r.ReadAmf0ObjectReference(),                             // 0x07 - reference
            r => r.ReadAmf0AssociativeArray(),                            // 0x08 - ECMA array
            r => { throw new NotSupportedException(); },                  // 0x09 - 'object end marker' - we handle this in deserializer block; we shouldn't encounter it here
            r => r.ReadAmf0Array(),                                       // 0x0A - strict array
            r => r.ReadAmf0Date(),                                        // 0x0B - date
            r => r.ReadAmf0LongString(),                                  // 0x0C - long string
            r => { throw new NotSupportedException(); },                  // 0x0D - 'unsupported marker'
            r => { throw new NotSupportedException(); },                  // 0x0E - recordset
            r => r.ReadAmf0XmlDocument(),                                 // 0x0F - xml document
            r => r.ReadAmf0Object(),                                      // 0x10 - typed object
            r => r.ReadAmf3Item()                                         // 0x11 - avmplus object
        };

        static readonly List<Func<AmfReader, CancellationToken, Task<object>>> Amf3AsyncTypeReaders = new List<Func<AmfReader, CancellationToken, Task<object>>>
        {
            (r, ct) => null,                                                    // 0x00 - undefined
            (r, ct) => null,                                                    // 0x01 - null
            (r, ct) => { var tsk = new TaskCompletionSource<object>(); tsk.SetResult(false); return tsk.Task; },      // 0x02 - false
            (r, ct) => { var tsk = new TaskCompletionSource<object>(); tsk.SetResult(true); return tsk.Task; },       // 0x03 - true
            async (r, ct) => await r.ReadAmf3IntAsync(ct),                                         // 0x04 - integer
            async (r, ct) => await r.ReadDoubleAsync(ct),                                          // 0x05 - double
            async (r, ct) => await r.ReadAmf3StringAsync(ct),                                      // 0x06 - string
            async (r, ct) => await r.ReadAmf3XmlDocumentAsync(ct),                                 // 0x07 - xml document
            async (r, ct) => await r.ReadAmf3DateAsync(ct),                                        // 0x08 - date
            async (r, ct) => await r.ReadAmf3ArrayAsync(ct),                                       // 0x09 - array
            async (r, ct) => await r.ReadAmf3ObjectAsync(ct),                                      // 0x0A - object
            async (r, ct) => await r.ReadAmf3XmlDocumentAsync(ct),                                 // 0x0B - xml
            async (r, ct) => await r.ReadAmf3ByteArrayAsync(ct),                                   // 0x0C - byte array
            async (r, ct) => await r.ReadAmf3VectorAsync(false, x => (int)x.ReadInt32(), ct),        // 0x0D - int vector
            async (r, ct) => await r.ReadAmf3VectorAsync(false, x => (uint)x.ReadInt32(), ct),       // 0x0E - uint vector
            async (r, ct) => await r.ReadAmf3VectorAsync(false, x => (double)x.ReadDouble(), ct),    // 0x0F - double vector
            async (r, ct) => await r.ReadAmf3VectorAsync(true, x => (object)x.ReadAmf3ItemAsync(), ct),   // 0x10 - object vector
            async (r, ct) => await r.ReadAmf3DictionaryAsync(ct),                                  // 0x11 - dictionary
        };

        static readonly List<Func<AmfReader, object>> Amf3TypeReaders = new List<Func<AmfReader, object>>
        {
            r => null,                                                    // 0x00 - undefined
            r => null,                                                    // 0x01 - null
            r => false,                                                   // 0x02 - false
            r => true,                                                    // 0x03 - true
            r => r.ReadAmf3Int(),                                         // 0x04 - integer
            r => r.ReadDouble(),                                          // 0x05 - double
            r => r.ReadAmf3String(),                                      // 0x06 - string
            r => r.ReadAmf3XmlDocument(),                                 // 0x07 - xml document
            r => r.ReadAmf3Date(),                                        // 0x08 - date
            r => r.ReadAmf3Array(),                                       // 0x09 - array
            r => r.ReadAmf3Object(),                                      // 0x0A - object
            r => r.ReadAmf3XmlDocument(),                                 // 0x0B - xml
            r => r.ReadAmf3ByteArray(),                                   // 0x0C - byte array
            r => r.ReadAmf3Vector(false, x => (int)x.ReadInt32()),        // 0x0D - int vector
            r => r.ReadAmf3Vector(false, x => (uint)x.ReadInt32()),       // 0x0E - uint vector
            r => r.ReadAmf3Vector(false, x => (double)x.ReadDouble()),    // 0x0F - double vector
            r => r.ReadAmf3Vector(true, x => (object)x.ReadAmf3ItemAsync()),   // 0x10 - object vector
            r => r.ReadAmf3Dictionary(),                                  // 0x11 - dictionary
        };
        private MemoryStream asyncBuffer;
        private Stream asyncBaseStream;
        private readonly int OnceReadBytes = 4096;

        public AmfReader(Stream stream, SerializationContext serializationContext, bool asyncMode = false)
        {
            this.asyncMode = asyncMode;
            if (asyncMode)
            {
                asyncBaseStream = stream;
                asyncBuffer = new MemoryStream(OnceReadBytes);
                underlying = new BinaryReader(asyncBuffer);
            }
            else
            {
                underlying = new BinaryReader(stream);
            }
            SerializationContext = serializationContext;

            amf0ObjectReferences = new List<object>();
            amf3ObjectReferences = new List<object>();
            stringReferences = new List<object>();
            amf3ClassDefinitions = new List<Amf3ClassDescription>();
        }

        public void Dispose()
        {
            underlying?.Dispose();
        }

        # region helpers

        public long Length => underlying.BaseStream.Length;
        public long Position => underlying.BaseStream.Position;
        public bool DataAvailable => Position < Length;
        

        public void Reset()
        {
            amf0ObjectReferences.Clear();
            amf3ObjectReferences.Clear();
            stringReferences.Clear();
            amf3ClassDefinitions.Clear();
        }

        public long Seek(int offset, SeekOrigin origin)
        {
            return underlying.BaseStream.Seek(offset, origin);
        }

        public byte ReadByte()
        {
            if (asyncMode)
                throw new InvalidOperationException("must use sync mode");
            return underlying.ReadByte();
        }

        public async Task<byte> ReadByteAsync(CancellationToken ct = default(CancellationToken))
        {
            if (!asyncMode)
                throw new InvalidOperationException("must use async mode");
            byte[] ret = await ReadBytesAsync(1, ct);
            return ret[0];
        }

        public byte[] ReadBytes(int count)
        {
            if (asyncMode)
                throw new InvalidOperationException("must use sync mode");
            return underlying.ReadBytes(count);
        }

        public async Task<byte[]> ReadBytesAsync(int count, CancellationToken ct = default(CancellationToken))
        {
            if (!asyncMode)
                throw new InvalidOperationException("must use async mode");
            if (asyncBuffer.Position + count > asyncBuffer.Length)
            {
                var remain_bytes = asyncBuffer.Length - asyncBuffer.Position;
                var orig_pos = asyncBuffer.Position;
                asyncBuffer.Seek(0, SeekOrigin.Begin);
                asyncBuffer.Write(asyncBuffer.GetBuffer(), (int)orig_pos, (int)remain_bytes);
                asyncBuffer.SetLength(remain_bytes);
                asyncBuffer.Seek(remain_bytes, SeekOrigin.Begin);
                asyncBuffer.SetLength(asyncBuffer.Capacity);
                int bytes_read = await asyncBaseStream.ReadAsync(asyncBuffer.GetBuffer(), (int)remain_bytes, (int)asyncBuffer.Capacity - (int)asyncBuffer.Position, ct);
                asyncBuffer.SetLength(bytes_read + remain_bytes);
                asyncBuffer.Seek(0, SeekOrigin.Begin);
            }
            byte[] ret = new byte[count];
            asyncBuffer.Read(ret, 0, count);

            return ret;
        }

        public ushort ReadUInt16()
        {
            var bytes = ReadBytes(2);
            return (ushort)(((bytes[0] & 0xFF) << 8) | (bytes[1] & 0xFF));
        }

        public async Task<ushort> ReadUInt16Async(CancellationToken ct = default(CancellationToken))
        {
            var bytes = await ReadBytesAsync(2, ct);
            return (ushort)(((bytes[0] & 0xFF) << 8) | (bytes[1] & 0xFF));
        }

        public short ReadInt16()
        {
            var bytes = ReadBytes(2);
            return (short)((bytes[0] << 8) | bytes[1]);
        }

        public async Task<short> ReadInt16Async(CancellationToken ct = default(CancellationToken))
        {
            var bytes = await ReadBytesAsync(2, ct);
            return (short)((bytes[0] << 8) | bytes[1]);
        }

        public bool ReadBoolean()
        {
            return underlying.ReadBoolean();
        }

        public async Task<bool> ReadBooleanAsync(CancellationToken ct = default(CancellationToken))
        {
            var bytes = await ReadBytesAsync(sizeof(bool), ct);
            return BitConverter.ToBoolean(bytes, 0);
        }

        public int ReadInt32()
        {
            var bytes = ReadBytes(4);
            return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        }

        public async Task<int> ReadInt32Async(CancellationToken ct = default(CancellationToken))
        {
            var bytes = await ReadBytesAsync(4, ct);
            return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        }

        public uint ReadUInt32()
        {
            var bytes = ReadBytes(4);
            return (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
        }

        public async Task<uint> ReadUInt32Async(CancellationToken ct = default(CancellationToken))
        {
            return (uint)(await ReadInt32Async(ct));
        }

        public int ReadReverseInt()
        {
            var bytes = ReadBytes(4);
            return (bytes[3] << 24) | (bytes[2] << 16) | (bytes[1] << 8) | bytes[0];
        }

        public async Task<int> ReadReverseIntAsync(CancellationToken ct = default(CancellationToken))
        {
            var bytes = await ReadBytesAsync(4, ct);
            return (bytes[3] << 24) | (bytes[2] << 16) | (bytes[1] << 8) | bytes[0];
        }

        public int ReadUInt24()
        {
            var bytes = ReadBytes(3);
            return bytes[0] << 16 | bytes[1] << 8 | bytes[2];
        }

        public async Task<int> ReadUInt24Async(CancellationToken ct = default(CancellationToken))
        {
            var bytes = await ReadBytesAsync(3, ct);
            return bytes[0] << 16 | bytes[1] << 8 | bytes[2];
        }

        // 64 bit IEEE-754 double precision floating point
        public double ReadDouble()
        {
            var bytes = ReadBytes(8);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return BitConverter.ToDouble(bytes, 0);
        }

        public async Task<double> ReadDoubleAsync(CancellationToken ct = default(CancellationToken))
        {
            var bytes = await ReadBytesAsync(8, ct);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return BitConverter.ToDouble(bytes, 0);
        }

        // single-precision floating point number 
        public float ReadFloat()
        {
            var bytes = ReadBytes(4);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return BitConverter.ToSingle(bytes, 0);
        }

        public async Task<float> ReadFloatAsync(CancellationToken ct = default(CancellationToken))
        {
            var bytes = await ReadBytesAsync(4, ct);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return BitConverter.ToSingle(bytes, 0);
        }

        // utf8 string with length prefix
        public string ReadUtf()
        {
            var stringLength = ReadUInt16();
            return ReadUtf(stringLength);
        }

        public async Task<string> ReadUtfAsync(CancellationToken ct = default(CancellationToken))
        {
            var stringLength = await ReadUInt16Async(ct);
            return await ReadUtfAsync(stringLength, ct);
        }

        // utf8 string
        public string ReadUtf(int length)
        {
            if (length == 0)
                return string.Empty;
            var bytes = ReadBytes(length);
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }

        public async Task<string> ReadUtfAsync(int length, CancellationToken ct = default(CancellationToken))
        {
            if (length == 0)
                return string.Empty;
            var bytes = await ReadBytesAsync(length, ct);
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }

        #endregion
        

        #region amf0

        public object ReadAmf0Item()
        {
            var type = ReadByte();
            return ReadAmf0Item(type);
        }

        public async Task<object> ReadAmf0ItemAsync(CancellationToken ct = default(CancellationToken))
        {
            var type = await ReadByteAsync(ct);
            return await ReadAmf0ItemAsync(type, ct);
        }

        object ReadAmf0Item(byte typeMarker)
        {
            return Amf0TypeReaders[typeMarker].Invoke(this);
        }

        async Task<object> ReadAmf0ItemAsync(byte typeMarker, CancellationToken ct = default(CancellationToken))
        {
            return await Amf0TypeAsyncReaders[typeMarker].Invoke(this, ct);
        }

        internal object ReadAmf0ObjectReference()
        {
            int reference = ReadUInt16();
            return amf0ObjectReferences[reference];
        }

        async internal Task<object> ReadAmf0ObjectReferenceAsync(CancellationToken ct = default(CancellationToken))
        {
            int reference = await ReadUInt16Async(ct);
            return amf0ObjectReferences[reference];
        }

        Dictionary<string, object> ReadAmf0Pairs()
        {
            return EnumerableReadAmf0Pairs().ToDictionary(x => x.Key, x => x.Value);
        }

        async Task<Dictionary<string, object>> ReadAmf0PairsAsync(CancellationToken ct = default(CancellationToken))
        {
            return (await EnumerableReadAmf0PairsAsync(ct)).ToDictionary(x => x.Key, x => x.Value);
        }

        // this method blocks when enumerating through the stream. consider using ReadAmf0Pairs() instead.
        IEnumerable<KeyValuePair<string, object>> EnumerableReadAmf0Pairs()
        {
            while (true)
            {
                var key = ReadUtf();
                var typeMarker = ReadByte();
                if (typeMarker == (byte)Amf0TypeMarkers.ObjectEnd)
                    yield break;

                var obj = ReadAmf0Item(typeMarker);
                yield return new KeyValuePair<string, object>(key, obj);
            }
        }

        async Task<List<KeyValuePair<string, object>>> EnumerableReadAmf0PairsAsync(CancellationToken ct = default(CancellationToken))
        {
            List<KeyValuePair<string, object>> ret = new List<KeyValuePair<string, object>>();
            while (true)
            {
                var key = await ReadUtfAsync(ct);
                var typeMarker = await ReadByteAsync(ct);
                if (typeMarker == (byte)Amf0TypeMarkers.ObjectEnd)
                    break;

                var obj = ReadAmf0Item(typeMarker);
                ret.Add(new KeyValuePair<string, object>(key, obj));
            }
            return ret;
        }

        void AddAmf0ObjectReference(object instance)
        {
            amf0ObjectReferences.Add(instance);
        }

        // amf0 object
        internal object ReadAmf0Object()
        {
            if (SerializationContext == null)
                throw new NullReferenceException("Cannot deserialize objects because no SerializationContext was provided.");

            var typeName = ReadUtf();
            var strategy = SerializationContext.GetDeserializationStrategy(typeName);
            switch (strategy)
            {
                case DeserializationStrategy.TypedObject:
                    var instance = SerializationContext.Create(typeName);
                    var classDescription = SerializationContext.GetClassDescription(instance);
                    var pairs = ReadAmf0Pairs();
                    foreach (var pair in pairs)
                    {
                        IMemberWrapper wrapper;
                        if (classDescription.TryGetMember(pair.Key, out wrapper))
                            wrapper.SetValue(instance, pair.Value);
                    }
                    return instance;

                case DeserializationStrategy.DynamicObject:
                    // object reference added in this.ReadAmf0AsObject()
                    var aso = ReadAmf0AsObject();
                    aso.TypeName = typeName;
                    return aso;

                default:
                case DeserializationStrategy.Exception:
                    throw new SerializationException($"can't deserialize a `{typeName}`");
            }
        }

        async internal Task<object> ReadAmf0ObjectAsync(CancellationToken ct = default(CancellationToken))
        {
            if (SerializationContext == null)
                throw new NullReferenceException("Cannot deserialize objects because no SerializationContext was provided.");

            var typeName = await ReadUtfAsync(ct);
            var strategy = SerializationContext.GetDeserializationStrategy(typeName);
            switch (strategy)
            {
                case DeserializationStrategy.TypedObject:
                    var instance = SerializationContext.Create(typeName);
                    var classDescription = SerializationContext.GetClassDescription(instance);
                    var pairs = await ReadAmf0PairsAsync(ct);
                    foreach (var pair in pairs)
                    {
                        IMemberWrapper wrapper;
                        if (classDescription.TryGetMember(pair.Key, out wrapper))
                            wrapper.SetValue(instance, pair.Value);
                    }
                    return instance;

                case DeserializationStrategy.DynamicObject:
                    // object reference added in this.ReadAmf0AsObject()
                    var aso = await ReadAmf0AsObjectAsync(ct);
                    aso.TypeName = typeName;
                    return aso;

                default:
                case DeserializationStrategy.Exception:
                    throw new SerializationException($"can't deserialize a `{typeName}`");
            }
        }

        internal AsObject ReadAmf0AsObject()
        {
            var obj = new AsObject(ReadAmf0Pairs());
            AddAmf0ObjectReference(obj);
            return obj;
        }

        async internal Task<AsObject> ReadAmf0AsObjectAsync(CancellationToken ct = default(CancellationToken))
        {
            var obj = new AsObject(await ReadAmf0PairsAsync(ct));
            AddAmf0ObjectReference(obj);
            return obj;
        }

        internal string ReadAmf0LongString()
        {
            var length = ReadInt32();
            return ReadUtf(length);
        }

        async internal Task<string> ReadAmf0LongStringAsync(CancellationToken ct = default(CancellationToken))
        {
            var length = await ReadInt32Async(ct);
            return await ReadUtfAsync(length, ct);
        }

        internal Dictionary<string, object> ReadAmf0AssociativeArray()
        {
            var length = ReadInt32();
            var obj = ReadAmf0Pairs();
            AddAmf0ObjectReference(obj);
            return obj;
        }

        async internal Task<Dictionary<string, object>> ReadAmf0AssociativeArrayAsync(CancellationToken ct = default(CancellationToken))
        {
            var length = await ReadInt32Async(ct);
            var obj = await ReadAmf0PairsAsync(ct);
            AddAmf0ObjectReference(obj);
            return obj;
        }

        internal object[] ReadAmf0Array()
        {
            var length = ReadInt32();
            var array = Enumerable.Range(0, length).Select(x => ReadAmf0Item()).ToArray();
            AddAmf0ObjectReference(array);
            return array;
        }

        async internal Task<object[]> ReadAmf0ArrayAsync(CancellationToken ct = default(CancellationToken))
        {
            var length = await ReadInt32Async(ct);
            var array = Enumerable.Range(0, length).Select(async x => await ReadAmf0ItemAsync(ct)).ToArray();
            AddAmf0ObjectReference(array);
            return array;
        }

        internal DateTime ReadAmf0Date()
        {
            var milliseconds = ReadDouble();
            var date = epoch.AddMilliseconds(milliseconds);

            // http://download.macromedia.com/pub/labs/amf/amf0_spec_121207.pdf
            // """
            // While the design of this type reserves room for time zone offset information,
            // it should not be filled in, nor used, as it is unconventional to change time
            // zones when serializing dates on a network. It is suggested that the time zone
            // be queried independently as needed.
            //  -- AMF0 specification, 2.13 Date Type
            // """
            int timeOffset = ReadUInt16();
            return date;
        }

        async internal Task<DateTime> ReadAmf0DateAsync(CancellationToken ct = default(CancellationToken))
        {
            var milliseconds = await ReadDoubleAsync(ct);
            var date = epoch.AddMilliseconds(milliseconds);
            
            int timeOffset = await ReadUInt16Async(ct);
            return date;
        }

        internal XDocument ReadAmf0XmlDocument()
        {
            var str = ReadAmf0LongString();
            return string.IsNullOrEmpty(str) ? new XDocument() : XDocument.Parse(str);
        }

        async internal Task<XDocument> ReadAmf0XmlDocumentAsync(CancellationToken ct = default(CancellationToken))
        {
            var str = await ReadAmf0LongStringAsync(ct);
            return string.IsNullOrEmpty(str) ? new XDocument() : XDocument.Parse(str);
        }

        #endregion










        #region amf3

        struct Amf3Field
        {
            public bool IsReference;
            public int Value;
        }

        public async Task<object> ReadAmf3ItemAsync(CancellationToken ct = default(CancellationToken))
        {
            var typeMarker = await ReadByteAsync(ct);
            return Amf3AsyncTypeReaders[typeMarker].Invoke(this, ct);
        }

        public object ReadAmf3Item()
        {
            var typeMarker = ReadByte();
            return Amf3TypeReaders[typeMarker].Invoke(this);
        }

        // returns a previously read object at `index`
        internal object GetAmf3ObjectReference(int index)
        {
            return amf3ObjectReferences[index];
        }

        Amf3Field ReadAmf3Field()
        {
            var data = ReadAmf3Int();
            return new Amf3Field()
            {
                IsReference = (data & 1) == 0, // 1 == inline object
                Value = data >> 1
            };
        }

        async Task<Amf3Field> ReadAmf3FieldAsync(CancellationToken ct = default(CancellationToken))
        {
            var data = await ReadAmf3IntAsync(ct);
            return new Amf3Field()
            {
                IsReference = (data & 1) == 0, // 1 == inline object
                Value = data >> 1
            };
        }

        // variable-length integer which uses the highest bit or each byte as a continuation flag.
        internal int ReadAmf3Int()
        {
            // http://download.macromedia.com/pub/labs/amf/Amf3_spec_121207.pdf
            // """
            // AMF 3 makes use of a special compact format for writing integers to reduce the
            // number of bytes required for encoding. As with a normal 32-bit integer, up to
            // 4 bytes are required to hold the value however the high bit of the first 3
            // bytes are used as flags to determine whether the next byte is part of the
            // integer. With up to 3 bits of the 32 bits being used as flags, only 29
            // significant bits remain for encoding an integer. This means the largest
            // unsigned integer value that can be represented is 2^29 - 1.
            // -- AMF3 specification, 1.3.1 Variable Length Unsigned 29-bit Integer Encoding
            // """

            // first byte
            int total = ReadByte();
            if (total < 128)
                return total;

            total = (total & 0x7f) << 7;
            // second byte
            int nextByte = ReadByte();
            if (nextByte < 128)
            {
                total = total | nextByte;
            }
            else
            {
                total = (total | nextByte & 0x7f) << 7;
                // third byte
                nextByte = ReadByte();
                if (nextByte < 128)
                {
                    total = total | nextByte;
                }
                else
                {
                    total = (total | nextByte & 0x7f) << 8;
                    // fourth byte
                    nextByte = ReadByte();
                    total = total | nextByte;
                }
            }

            // to sign extend a value from some number of bits to a greater number of bits just copy the sign bit into all the additional bits in the new format.
            // convert / sign extend the 29-bit two's complement number to 32 bit
            const int mask = 1 << 28;
            return -(total & mask) | total;
        }

                // variable-length integer which uses the highest bit or each byte as a continuation flag.
        internal async Task<int> ReadAmf3IntAsync(CancellationToken ct = default(CancellationToken))
        {
            // http://download.macromedia.com/pub/labs/amf/Amf3_spec_121207.pdf
            // """
            // AMF 3 makes use of a special compact format for writing integers to reduce the
            // number of bytes required for encoding. As with a normal 32-bit integer, up to
            // 4 bytes are required to hold the value however the high bit of the first 3
            // bytes are used as flags to determine whether the next byte is part of the
            // integer. With up to 3 bits of the 32 bits being used as flags, only 29
            // significant bits remain for encoding an integer. This means the largest
            // unsigned integer value that can be represented is 2^29 - 1.
            // -- AMF3 specification, 1.3.1 Variable Length Unsigned 29-bit Integer Encoding
            // """

            // first byte
            int total = await ReadByteAsync(ct);
            if (total < 128)
                return total;

            total = (total & 0x7f) << 7;
            // second byte
            int nextByte = await ReadByteAsync(ct);
            if (nextByte < 128)
            {
                total = total | nextByte;
            }
            else
            {
                total = (total | nextByte & 0x7f) << 7;
                // third byte
                nextByte = await ReadByteAsync(ct);
                if (nextByte < 128)
                {
                    total = total | nextByte;
                }
                else
                {
                    total = (total | nextByte & 0x7f) << 8;
                    // fourth byte
                    nextByte = await ReadByteAsync(ct);
                    total = total | nextByte;
                }
            }

            // to sign extend a value from some number of bits to a greater number of bits just copy the sign bit into all the additional bits in the new format.
            // convert / sign extend the 29-bit two's complement number to 32 bit
            const int mask = 1 << 28;
            return -(total & mask) | total;
        }

        internal DateTime ReadAmf3Date()
        {
            var header = ReadAmf3Field();
            if (header.IsReference)
                return (DateTime)GetAmf3ObjectReference(header.Value);

            var milliseconds = ReadDouble();
            var date = epoch.AddMilliseconds(milliseconds);
            amf3ObjectReferences.Add(date);
            return date;
        }

        internal async Task<DateTime> ReadAmf3DateAsync(CancellationToken ct = default(CancellationToken))
        {
            var header = await ReadAmf3FieldAsync(ct);
            if (header.IsReference)
                return (DateTime)GetAmf3ObjectReference(header.Value);

            var milliseconds = await ReadDoubleAsync(ct);
            var date = epoch.AddMilliseconds(milliseconds);
            amf3ObjectReferences.Add(date);
            return date;
        }

        internal string ReadAmf3String()
        {
            var header = ReadAmf3Field();
            if (header.IsReference)
                return stringReferences[header.Value] as string;

            var length = header.Value;
            if (length == 0)
                return string.Empty;

            var str = ReadUtf(length);
            stringReferences.Add(str);
            return str;
        }

        internal async Task<string> ReadAmf3StringAsync(CancellationToken ct = default(CancellationToken))
        {
            var header = await ReadAmf3FieldAsync(ct);
            if (header.IsReference)
                return stringReferences[header.Value] as string;

            var length = header.Value;
            if (length == 0)
                return string.Empty;

            var str = await ReadUtfAsync(length, ct);
            stringReferences.Add(str);
            return str;
        }

        internal XDocument ReadAmf3XmlDocument()
        {
            string xml;
            var header = ReadAmf3Field();
            if (header.IsReference)
            {
                xml = GetAmf3ObjectReference(header.Value) as string;
            }
            else
            {
                xml = header.Value > 0 ? ReadUtf(header.Value) : string.Empty;
                amf3ObjectReferences.Add(xml);
            }
            return string.IsNullOrEmpty(xml) ? new XDocument() : XDocument.Parse(xml);
        }

        internal async Task<XDocument> ReadAmf3XmlDocumentAsync(CancellationToken ct = default(CancellationToken))
        {
            string xml;
            var header = await ReadAmf3FieldAsync(ct);
            if (header.IsReference)
            {
                xml = GetAmf3ObjectReference(header.Value) as string;
            }
            else
            {
                xml = header.Value > 0 ? await ReadUtfAsync(header.Value, ct) : string.Empty;
                amf3ObjectReferences.Add(xml);
            }
            return string.IsNullOrEmpty(xml) ? new XDocument() : XDocument.Parse(xml);
        }

        internal ByteArray ReadAmf3ByteArray()
        {
            var header = ReadAmf3Field();
            if (header.IsReference)
                return GetAmf3ObjectReference(header.Value) as ByteArray;

            var length = header.Value;
            var byteArray = new ByteArray(ReadBytes(length), SerializationContext);
            amf3ObjectReferences.Add(byteArray);
            return byteArray;
        }

        internal async Task<ByteArray> ReadAmf3ByteArrayAsync(CancellationToken ct = default(CancellationToken))
        {
            var header = await ReadAmf3FieldAsync(ct);
            if (header.IsReference)
                return GetAmf3ObjectReference(header.Value) as ByteArray;

            var length = header.Value;
            var byteArray = new ByteArray(await ReadBytesAsync(length, ct), SerializationContext);
            amf3ObjectReferences.Add(byteArray);
            return byteArray;
        }

        internal object ReadAmf3Array()
        {
            var header = ReadAmf3Field();
            if (header.IsReference)
                return GetAmf3ObjectReference(header.Value);

            var key = ReadAmf3String();
            var hasAssociative = !string.IsNullOrEmpty(key);

            var associative = new Dictionary<string, object>();
            if (hasAssociative)
                amf3ObjectReferences.Add(associative);

            // associative elements
            while (!string.IsNullOrEmpty(key))
            {
                var value = ReadAmf3Item();
                associative.Add(key, value);

                key = ReadAmf3String();
            }

            // strict array elements
            var length = header.Value;
            var array = new object[length];
            if (!hasAssociative)
                amf3ObjectReferences.Add(array);
            for (var i = 0; i < length; i++)
                array[i] = ReadAmf3Item();

            // merge associative and strict elements, if there's an associative
            // otherwise return strict array
            if (hasAssociative)
            {
                for (var i = 0; i < array.Length; i++)
                    associative.Add(i.ToString(CultureInfo.InvariantCulture), array[i]);
                return associative;
            }

            return array;
        }

        internal async Task<object> ReadAmf3ArrayAsync(CancellationToken ct = default(CancellationToken))
        {
            var header = await ReadAmf3FieldAsync(ct);
            if (header.IsReference)
                return GetAmf3ObjectReference(header.Value);

            var key = await ReadAmf3StringAsync(ct);
            var hasAssociative = !string.IsNullOrEmpty(key);

            var associative = new Dictionary<string, object>();
            if (hasAssociative)
                amf3ObjectReferences.Add(associative);

            // associative elements
            while (!string.IsNullOrEmpty(key))
            {
                var value = await ReadAmf3ItemAsync(ct);
                associative.Add(key, value);

                key = await ReadAmf3StringAsync(ct);
            }

            // strict array elements
            var length = header.Value;
            var array = new object[length];
            if (!hasAssociative)
                amf3ObjectReferences.Add(array);
            for (var i = 0; i < length; i++)
                array[i] = await ReadAmf3ItemAsync(ct);

            // merge associative and strict elements, if there's an associative
            // otherwise return strict array
            if (hasAssociative)
            {
                for (var i = 0; i < array.Length; i++)
                    associative.Add(i.ToString(CultureInfo.InvariantCulture), array[i]);
                return associative;
            }

            return array;
        }

        internal object ReadAmf3Vector<T>(bool hasTypeName, Func<AmfReader, T> readElement)
        {
            var header = ReadAmf3Field();
            if (header.IsReference)
                return GetAmf3ObjectReference(header.Value);

            var itemCount = header.Value;
            var fixedSize = ReadByte() == 0x01;
            var list = new List<T>(itemCount);
            amf3ObjectReferences.Add(list);

            var typeName = hasTypeName ? ReadAmf3String() : null;
            for (var i = 0; i < itemCount; i++)
                list.Add(readElement(this));

            if (fixedSize)
                return list.ToArray();
            return list;
        }

        internal async Task<object> ReadAmf3VectorAsync<T>(bool hasTypeName, Func<AmfReader, T> readElement, CancellationToken ct = default(CancellationToken))
        {
            var header = await ReadAmf3FieldAsync();
            if (header.IsReference)
                return GetAmf3ObjectReference(header.Value);

            var itemCount = header.Value;
            var fixedSize = await ReadByteAsync() == 0x01;
            var list = new List<T>(itemCount);
            amf3ObjectReferences.Add(list);

            var typeName = hasTypeName ? await ReadAmf3StringAsync() : null;
            for (var i = 0; i < itemCount; i++)
                list.Add(readElement(this));

            if (fixedSize)
                return list.ToArray();
            return list;
        }

        internal object ReadAmf3Dictionary()
        {
            var header = ReadAmf3Field();
            if (header.IsReference)
                return GetAmf3ObjectReference(header.Value);

            var itemCount = header.Value;
            var isWeak = ReadByte() == 0x01;
            var dictionary = new Dictionary<object, object>(itemCount);
            amf3ObjectReferences.Add(dictionary);

            var wrapObject = new Func<object, object>(obj => isWeak ? new WeakReference(obj) : obj);
            for (int i = 0; i < itemCount; i++)
            {
                var key = ReadAmf3Item();
                var value = ReadAmf3Item();
                dictionary.Add(wrapObject(key), wrapObject(value));
            }
            return dictionary;
        }

        internal async Task<object> ReadAmf3DictionaryAsync(CancellationToken ct = default(CancellationToken))
        {
            var header = await ReadAmf3FieldAsync(ct);
            if (header.IsReference)
                return GetAmf3ObjectReference(header.Value);

            var itemCount = header.Value;
            var isWeak = await ReadByteAsync() == 0x01;
            var dictionary = new Dictionary<object, object>(itemCount);
            amf3ObjectReferences.Add(dictionary);

            var wrapObject = new Func<object, object>(obj => isWeak ? new WeakReference(obj) : obj);
            for (int i = 0; i < itemCount; i++)
            {
                var key = await ReadAmf3ItemAsync(ct);
                var value = await ReadAmf3ItemAsync(ct);
                dictionary.Add(wrapObject(key), wrapObject(value));
            }
            return dictionary;
        }

        Amf3ClassDescription ReadClassDefinition(int flags)
        {
            var isReference = (flags & 1) == 0;
            if (isReference)
                return amf3ClassDefinitions[flags >> 1];

            var typeName = ReadAmf3String();
            var externalizable = ((flags >> 1) & 1) != 0;
            var dynamic = ((flags >> 2) & 1) != 0;
            var memberCount = flags >> 3;

            var memberNames = Enumerable.Range(0, memberCount).Select(i => ReadAmf3String()).ToArray();
            var classDefinition = new Amf3ClassDescription()
            {
                TypeName = typeName,
                MemberNames = memberNames,
                IsExternalizable = externalizable,
                IsDynamic = dynamic
            };
            amf3ClassDefinitions.Add(classDefinition);
            return classDefinition;
        }

        async Task<Amf3ClassDescription> ReadClassDefinitionAsync(int flags, CancellationToken ct)
        {
            var isReference = (flags & 1) == 0;
            if (isReference)
                return amf3ClassDefinitions[flags >> 1];

            var typeName = await ReadAmf3StringAsync(ct);
            var externalizable = ((flags >> 1) & 1) != 0;
            var dynamic = ((flags >> 2) & 1) != 0;
            var memberCount = flags >> 3;

            var memberNames = new List<string>();
            for (var i = 0; i < memberCount; i++)
            {
                memberNames.Add(await ReadAmf3StringAsync(ct));
            }

            var classDefinition = new Amf3ClassDescription()
            {
                TypeName = typeName,
                MemberNames = memberNames.ToArray(),
                IsExternalizable = externalizable,
                IsDynamic = dynamic
            };
            amf3ClassDefinitions.Add(classDefinition);
            return classDefinition;
        }

        internal object ReadAmf3Object()
        {
            if (SerializationContext == null)
                throw new NullReferenceException("no serialization context was provided");

            var header = ReadAmf3Field();
            if (header.IsReference)
                return GetAmf3ObjectReference(header.Value);

            var @class = ReadClassDefinition(header.Value);

            var strategy = SerializationContext.GetDeserializationStrategy(@class.TypeName);
            if (strategy == DeserializationStrategy.Exception)
                throw new SerializationException($"can't deserialize a `{@class.TypeName}`");

            var instance = @class.IsTyped && strategy == DeserializationStrategy.TypedObject
                ? SerializationContext.Create(@class.TypeName)
                : new AsObject(@class.TypeName);
            amf3ObjectReferences.Add(instance);

            if (@class.IsExternalizable)
            {
                var externalizable = instance as IExternalizable;
                if (externalizable == null)
                    throw new SerializationException($"{@class.TypeName} does not implement IExternalizable");

                externalizable.ReadExternal(new DataInput(this));
            }
            else
            {
                var classDescription = SerializationContext.GetClassDescription(instance);
                foreach (var memberName in @class.MemberNames)
                {
                    IMemberWrapper member;
                    var value = ReadAmf3Item();
                    if (classDescription.TryGetMember(memberName, out member))
                        member.SetValue(instance, value);
                }

                if (@class.IsDynamic)
                {
                    while (true)
                    {
                        var key = ReadAmf3String();
                        if (string.IsNullOrEmpty(key))
                            break;
                        var obj = ReadAmf3Item();

                        IMemberWrapper wrapper;
                        if (classDescription.TryGetMember(key, out wrapper))
                            wrapper.SetValue(instance, obj);
                    }
                }
            }
            return instance;
        }

        internal async Task<object> ReadAmf3ObjectAsync(CancellationToken ct = default(CancellationToken))
        {
            if (SerializationContext == null)
                throw new NullReferenceException("no serialization context was provided");

            var header = await ReadAmf3FieldAsync(ct);
            if (header.IsReference)
                return GetAmf3ObjectReference(header.Value);

            var @class = await ReadClassDefinitionAsync(header.Value, ct);

            var strategy = SerializationContext.GetDeserializationStrategy(@class.TypeName);
            if (strategy == DeserializationStrategy.Exception)
                throw new SerializationException($"can't deserialize a `{@class.TypeName}`");

            var instance = @class.IsTyped && strategy == DeserializationStrategy.TypedObject
                ? SerializationContext.Create(@class.TypeName)
                : new AsObject(@class.TypeName);
            amf3ObjectReferences.Add(instance);

            if (@class.IsExternalizable)
            {
                var externalizable = instance as IExternalizable;
                if (externalizable == null)
                    throw new SerializationException($"{@class.TypeName} does not implement IExternalizable");

                await externalizable.ReadExternalAsync(new DataInput(this), ct);
            }
            else
            {
                var classDescription = SerializationContext.GetClassDescription(instance);
                foreach (var memberName in @class.MemberNames)
                {
                    IMemberWrapper member;
                    var value = await ReadAmf3ItemAsync(ct);
                    if (classDescription.TryGetMember(memberName, out member))
                        member.SetValue(instance, value);
                }

                if (@class.IsDynamic)
                {
                    while (true)
                    {
                        var key = await ReadAmf3StringAsync(ct);
                        if (string.IsNullOrEmpty(key))
                            break;
                        var obj = await ReadAmf3ItemAsync(ct);

                        IMemberWrapper wrapper;
                        if (classDescription.TryGetMember(key, out wrapper))
                            wrapper.SetValue(instance, obj);
                    }
                }
            }
            return instance;
        }

        #endregion
    }
}
