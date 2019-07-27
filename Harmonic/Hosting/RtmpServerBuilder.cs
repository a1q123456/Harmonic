using Harmonic.Controllers;
using Harmonic.Controllers.Living;
using Harmonic.Networking.Rtmp;
using System;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace Harmonic.Hosting
{
    public class RtmpServerBuilder
    {
        private IStartup _startup = null;
        private X509Certificate2 _cert = null;
        private bool _useWebsocket = false;
        private bool _useSsl = false;
        private int _bindRtmpPort = 1935;
        private int _bindWebsocketPort = -1;
        private bool _usingRtmp = false;
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

        public RtmpServerBuilder UseHarmonic(Action<RtmpServerOptions> config)
        {
            _usingRtmp = true;
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

            foreach (var type in types)
            {
                if (typeof(AbstractController).IsAssignableFrom(type) && !type.IsAbstract)
                {
                    _options.RegisterController(type);
                }
                if (typeof(LivingController).IsAssignableFrom(type))
                {
                    registerInternalControllers = false;
                }
            }

            if (registerInternalControllers)
            {
                _options.RegisterController<LivingController>();
                _options.RegisterStream<LivingStream>();
            }
            
            _options.BuildContainer();
            var ret = new RtmpServer(_options);
            return ret;
        }

    }
}