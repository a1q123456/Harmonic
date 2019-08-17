using Harmonic.Networking.Rtmp.Data;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Harmonic.Networking.Rtmp
{
    public class RtmpChunkStream : IDisposable
    {
        internal RtmpSession RtmpSession { get; set; } = null;
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
        private bool disposedValue = false;

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
}
