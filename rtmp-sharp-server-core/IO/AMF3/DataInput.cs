using System;
using System.Threading;
using System.Threading.Tasks;

namespace RtmpSharp.IO.AMF3
{
    class DataInput : IDataInput
    {
        private readonly AmfReader reader;
        private ObjectEncoding objectEncoding;

        public DataInput(AmfReader reader)
        {
            this.reader = reader;
            this.objectEncoding = ObjectEncoding.Amf3;
        }

        public ObjectEncoding ObjectEncoding
        {
            get { return objectEncoding; }
            set { objectEncoding = value; }
        }

        public object ReadObject()
        {
            switch (objectEncoding)
            {
                case ObjectEncoding.Amf0:
                    return reader.ReadAmf0Item();
                case ObjectEncoding.Amf3:
                    return reader.ReadAmf3Item();
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public async Task<object> ReadObjectAsync(CancellationToken ct = default)
        {
            switch (objectEncoding)
            {
                case ObjectEncoding.Amf0:
                    return await reader.ReadAmf0ItemAsync(ct);
                case ObjectEncoding.Amf3:
                    return await reader.ReadAmf3ItemAsync(ct);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public bool ReadBoolean() => reader.ReadBoolean();
        public byte ReadByte() => reader.ReadByte();
        public byte[] ReadBytes(int count) => reader.ReadBytes(count);
        public double ReadDouble() => reader.ReadDouble();
        public float ReadFloat() => reader.ReadFloat();
        public short ReadInt16() => reader.ReadInt16();
        public ushort ReadUInt16() => reader.ReadUInt16();
        public int ReadUInt24() => reader.ReadUInt24();
        public int ReadInt32() => reader.ReadInt32();
        public uint ReadUInt32() => reader.ReadUInt32();
        public string ReadUtf() => reader.ReadUtf();
        public string ReadUtf(int length) => reader.ReadUtf(length);
        public async Task<bool> ReadBooleanAsync(CancellationToken ct = default) => await reader.ReadBooleanAsync(ct);
        public async Task<byte> ReadByteAsync(CancellationToken ct = default) => await reader.ReadByteAsync(ct);
        public async Task<byte[]> ReadBytesAsync(int count, CancellationToken ct = default) => await reader.ReadBytesAsync(count, ct);
        public async Task<double> ReadDoubleAsync(CancellationToken ct = default) => await reader.ReadDoubleAsync(ct);
        public async Task<float> ReadFloatAsync(CancellationToken ct = default) => await reader.ReadFloatAsync(ct);
        public async Task<short> ReadInt16Async(CancellationToken ct = default) => await reader.ReadInt16Async(ct);
        public async Task<ushort> ReadUInt16Async(CancellationToken ct = default) => await reader.ReadUInt16Async(ct);
        public async Task<int> ReadUInt24Async(CancellationToken ct = default) => await reader.ReadUInt24Async(ct);
        public async Task<int> ReadInt32Async(CancellationToken ct = default) => await reader.ReadInt32Async(ct);
        public async Task<uint> ReadUInt32Async(CancellationToken ct = default) => await reader.ReadUInt32Async(ct);
        public async Task<string> ReadUtfAsync(CancellationToken ct = default) => await reader.ReadUtfAsync(ct);
        public async Task<string> ReadUtfAsync(int length, CancellationToken ct = default) => await reader.ReadUtfAsync(length, ct);
    }
}
