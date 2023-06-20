namespace Harmonic.Networking.Rtmp;

class RtmpControlChunkStream : RtmpChunkStream
{
    private static readonly uint _controlCsid = 2;

    internal RtmpControlChunkStream(RtmpSession rtmpSession) : base()
    {
        this.ChunkStreamId = _controlCsid;
        this.RtmpSession = rtmpSession;
    }
}