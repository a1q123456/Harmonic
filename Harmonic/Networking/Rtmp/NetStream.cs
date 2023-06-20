using Harmonic.Controllers;
using Harmonic.Rpc;
using System;

namespace Harmonic.Networking.Rtmp;

public abstract class NetStream : RtmpController, IDisposable
{
    [RpcMethod("deleteStream")]
    public void DeleteStream()
    {
        Dispose();
    }

    #region IDisposable Support
    private bool disposedValue = false;

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                MessageStream.RtmpSession.NetConnection.MessageStreamDestroying(this);
            }

            disposedValue = true;
        }
    }

    // ~NetStream() {
    //   Dispose(false);
    // }

    public void Dispose()
    {
        Dispose(true);
        // GC.SuppressFinalize(this);
    }
    #endregion

}