using System;

namespace Harmonic.Networking.Rtmp;

public class RtmpChunkStream : IDisposable
{
    internal RtmpSession RtmpSession { get; set; }
    public uint ChunkStreamId { get; protected set; }

    internal RtmpChunkStream(RtmpSession rtmpSession, uint chunkStreamId)
    {
        ChunkStreamId = chunkStreamId;
        RtmpSession = rtmpSession;
    }

    internal RtmpChunkStream(RtmpSession rtmpSession)
    {
        RtmpSession = rtmpSession;
        ChunkStreamId = rtmpSession.MakeUniqueChunkStreamId();
    }

    protected RtmpChunkStream()
    {

    }

    #region IDisposable Support
    private bool disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                RtmpSession.ChunkStreamDestroyed(this);
            }

            disposedValue = true;
        }
    }

    // ~RtmpChunkStream() {
    //   Dispose(false);
    // }

    public void Dispose()
    {
        Dispose(true);
        // GC.SuppressFinalize(this);
    }
    #endregion

}