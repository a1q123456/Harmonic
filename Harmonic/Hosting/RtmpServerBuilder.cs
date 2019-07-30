using Harmonic.Controllers;
using Harmonic.Controllers.Living;
using Harmonic.NetWorking.Rtmp;
using System;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace Harmonic.Hosting
{
    public class RtmpServerBuilder
    {
        private IStartup _startup = null;
        private X509Certificate2 _cert = null;
        private bool _useWebSocket = false;
        private bool _useSsl = false;
        private WebSocketOptions _websocketOptions = null;

        private RtmpServerOptions _options = null;

        public RtmpServerBuilder UseStartup<T>() where T: IStartup, new()
        {
            _startup = new T();
            return this;
        }
        public RtmpServerBuilder UseSsl(X509Certificate2 cert)
        {
            _useSsl = true;
            _cert = cert;
            return this;
        }

        public RtmpServerBuilder UseWebSocket(Action<WebSocketOptions> conf)
        {
            _useWebSocket = true;
            _websocketOptions = new WebSocketOptions();
            conf(_websocketOptions);
            return this;
        }

        public RtmpServerBuilder UseHarmonic(Action<RtmpServerOptions> config)
        {
            _options = new RtmpServerOptions();
            config(_options);
            return this;
        }

        public RtmpServer Build()
        {
            _options = _options ?? new RtmpServerOptions();
            _options.Startup = _startup;
            var types = Assembly.GetCallingAssembly().GetTypes();

            var registerInternalControllers = true;
            _websocketOptions._serverOptions = _options;
            foreach (var type in types)
            {
                if (typeof(NetStream).IsAssignableFrom(type) && !type.IsAbstract)
                {
                    _options.RegisterStream(type);
                }
                else if (typeof(RtmpController).IsAssignableFrom(type) && !type.IsAbstract)
                {
                    _options.RegisterController(type);
                }

                if (typeof(LivingController).IsAssignableFrom(type))
                {
                    registerInternalControllers = false;
                }
                if (_useWebSocket)
                {
                    if (typeof(WebSocketController).IsAssignableFrom(type) && !type.IsAbstract)
                    {
                        _websocketOptions.RegisterController(type);
                    }
                    if (typeof(WebSocketPlayController).IsAssignableFrom(type))
                    {
                        registerInternalControllers = false;
                    }
                }
            }

            if (registerInternalControllers)
            {
                _options.RegisterController<LivingController>();
                _options.RegisterStream<LivingStream>();
                if (_useWebSocket)
                {
                    _websocketOptions.RegisterController<WebSocketPlayController>();
                }
            }
           
            if (_useSsl)
            {
                _options.Cert = _cert;
            }
            _options.CleanupRpcRegistration();
            _options.BuildContainer();
            var ret = new RtmpServer(_options, _websocketOptions);
            return ret;
        }

    }
}