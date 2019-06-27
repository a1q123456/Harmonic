using System;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace Harmonic.Hosting
{
    public class RtmpServerBuilder
    {
        private IStartup _startUp = null;
        private X509Certificate2 _cert = null;
        private ObjectEncoding _objectEncoding = ObjectEncoding.Amf0;
        private string _bindRtmpIp = "0.0.0.0";
        private string _bindwebsocketIP = "0.0.0.0";
        private bool _useWebsocket = false;
        private bool _useSsl = false;
        private int _bindRtmpPort = 1935;
        private int _bindWebsocketPort = -1;
        private bool _usingRtmp = false;

        public RtmpServerBuilder UseStartup(IStartup startup)
        {
            _startUp = startup;
            return this;
        }
        public RtmpServerBuilder UseSsl(X509Certificate2 cert)
        {
            _useSsl = true;
            _cert = cert;
            return this;
        }

        public RtmpServerBuilder UseWebsocket(Action<WebsocketOptions> config)
        {
            var op = new WebsocketOptions();
            config(op);
            _bindWebsocketPort = op.Port;
            _bindwebsocketIP = op.IPAddress;
            return this;
        }

        public RtmpServerBuilder UseRtmp(Action<RtmpOptions> config)
        {
            _usingRtmp = true;
            var op = new RtmpOptions();
            config(op);
            _serializationContext = op.SerializationContext;
            _objectEncoding = op.ObjectEncoding;
            _bindRtmpIp = op.IPAddress;
            _bindRtmpPort = op.Port;
            return this;
        }

        public RtmpServer Build()
        {
            var ret = new RtmpServer(_startUp, 
                                _serializationContext, 
                                _useSsl,
                                _cert, 
                                _objectEncoding, 
                                _bindRtmpIp,
                                !_usingRtmp && _useSsl ? 443 : _bindRtmpPort,
                                _useWebsocket,
                                _bindwebsocketIP,
                                _bindWebsocketPort);
            var types = Assembly.GetCallingAssembly().GetTypes();

            var registerInternalControllers = true;

            foreach (var type in types)
            {
                if (type.IsAssignableFrom(typeof(AbstractController)))
                {
                    ret.RegisterController(type);
                }
                if (type.IsAssignableFrom(typeof(LivingController)))
                {
                    registerInternalControllers = false;
                }
            }

            if (registerInternalControllers)
            {
                ret.RegisterController<LivingController>();
            }

            return ret;
        }

    }
}