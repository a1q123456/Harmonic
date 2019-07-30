using Harmonic.NetWorking.Rtmp;
using Harmonic.NetWorking.WebSocket;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Controllers
{
    public abstract class WebSocketController
    {
        public string StreamName { get; internal set; }
        public string Query { get; internal set; }
        public WebSocketSession Session { get; internal set; }

        public abstract void OnConnect();

        public abstract void OnMessage(string msg);
    }
}
