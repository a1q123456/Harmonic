
using System.Threading;
using System.Threading.Tasks;

namespace RtmpSharp.IO.AMF3
{
    public interface IDataInput
    {
        object ReadObject();
        bool ReadBoolean();
        byte ReadByte();
        byte[] ReadBytes(int count);
        double ReadDouble();
        float ReadFloat();
        short ReadInt16();
        ushort ReadUInt16();
        int ReadUInt24();
        int ReadInt32();
        uint ReadUInt32();
        string ReadUtf();
        string ReadUtf(int length);
        Task<object> ReadObjectAsync(CancellationToken ct = default);
        Task<bool> ReadBooleanAsync(CancellationToken ct = default);
        Task<byte> ReadByteAsync(CancellationToken ct = default);
        Task<byte[]> ReadBytesAsync(int count, CancellationToken ct = default);
        Task<double> ReadDoubleAsync(CancellationToken ct = default);
        Task<float> ReadFloatAsync(CancellationToken ct = default);
        Task<short> ReadInt16Async(CancellationToken ct = default);
        Task<ushort> ReadUInt16Async(CancellationToken ct = default);
        Task<int> ReadUInt24Async(CancellationToken ct = default);
        Task<int> ReadInt32Async(CancellationToken ct = default);
        Task<uint> ReadUInt32Async(CancellationToken ct = default);
        Task<string> ReadUtfAsync(CancellationToken ct = default);
        Task<string> ReadUtfAsync(int length, CancellationToken ct = default);
    }
}