namespace Harmonic.Networking.Rtmp.Data;

class ChunkBasicHeader 
{
    public ChunkHeaderType RtmpChunkHeaderType { get; set; }
    public uint ChunkStreamId { get; set; }
}