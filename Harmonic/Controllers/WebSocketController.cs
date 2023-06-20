using Harmonic.Networking.Flv;
using Harmonic.Networking.WebSocket;
using System.Collections.Specialized;
using System.Threading.Tasks;

namespace Harmonic.Controllers;

public abstract class WebSocketController
{
    public string StreamName { get; internal set; }
    public NameValueCollection Query { get; internal set; }
    public WebSocketSession Session { get; internal set; }
    private FlvMuxer _flvMuxer = null;
    private FlvDemuxer _flvDemuxer = null;

    public FlvMuxer FlvMuxer
    {
        get
        {
            if (_flvMuxer == null)
            {
                _flvMuxer = new FlvMuxer();
            }
            return _flvMuxer;
        }
    }
    public FlvDemuxer FlvDemuxer
    {
        get
        {
            if (_flvDemuxer == null)
            {
                _flvDemuxer = new FlvDemuxer(Session.Options.MessageFactories);
            }
            return _flvDemuxer;
        }
    }
    public abstract Task OnConnect();

    public abstract void OnMessage(string msg);
}