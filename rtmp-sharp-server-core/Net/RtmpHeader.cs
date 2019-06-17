
namespace RtmpSharp.Net
{
    public class ChunkHeader
    {
        // size of the chunk, including the header and payload
        public uint PacketLength { get; set; }
        public int ChunkStreamId { get; set; } = -1;
        public MessageType MessageType { get; set; }
        public int MessageStreamId { get; set; } = 0;
        // Maybe relative or absolute denpends on chunk header type
        public uint Timestamp { get; set; }
        // always absolute
        public uint AbsoluteTimestamp { get; set; }
        public ChunkMessageHeaderType ChunkMessageHeaderType { get; set; }
        public bool IsRelativeTimestamp => ChunkMessageHeaderType != ChunkMessageHeaderType.Complete;

        public static int GetHeaderLength(ChunkMessageHeaderType chunkMessageHeaderType)
        {
            switch (chunkMessageHeaderType)
            {
                case ChunkMessageHeaderType.Complete:
                    return 11;
                case ChunkMessageHeaderType.SameMessageStreamId:
                    return 7;
                case ChunkMessageHeaderType.OnlyTimestampNotSame:
                    return 3;
                case ChunkMessageHeaderType.AllSame:
                    return 0;
                default:
                    return -1;
            }
        }

        public ChunkHeader Clone()
        {
            return (ChunkHeader)this.MemberwiseClone();
        }
     
        // static string[] headerTypeNames = { "unknown", "chunk_size", "unknown2", "bytes_read", "ping", "server_bw", "client_bw", "unknown7", "audio", "video", "unknown10", "unknown11", "unknown12", "unknown13", "unknown14", "flex_stream", "flex_shared_object", "flex_message", "notify", "shared_object", "invoke" };
    }
}
