
namespace RtmpSharp.Net
{
    public enum ChunkMessageHeaderType : byte
    {
        Complete = 0,
        SameMessageStreamId = 1,
        OnlyTimestampNotSame = 2,
        AllSame = 3
    }
}
