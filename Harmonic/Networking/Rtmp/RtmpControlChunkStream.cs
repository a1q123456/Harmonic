namespace Harmonic.Networking.Rtmp;

class RtmpControlChunkStream : RtmpChunkStream
{
    private static readonly uint CONTROL_CSID = 2;

    internal RtmpControlChunkStream(RtmpSession rtmpSession) : base()
    {
        ChunkStreamId = CONTROL_CSID;
        RtmpSession = rtmpSession;
    }
}