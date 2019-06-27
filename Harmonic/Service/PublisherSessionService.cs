using System;
using System.Linq;
using System.Collections.Generic;
using Harmonic.NetWorking;

namespace Harmonic.Service
{
    public class PublisherSessionService
    {
        private Dictionary<string, IStreamSession> _pathMapToSession = new Dictionary<string, IStreamSession>();
        private Dictionary<IStreamSession, string> _sessionMapToPath = new Dictionary<IStreamSession, string>();

        public PublisherSessionService() {}

        public void RegisterPublisher(string path, IStreamSession session)
        {
            if (_pathMapToSession.ContainsKey(path))
            {
                throw new InvalidOperationException("request instance is publishing");
            }
            if (_sessionMapToPath.ContainsKey(session))
            {
                throw new InvalidOperationException("request session is publishing");
            }
            _pathMapToSession.Add(path, session);
            _sessionMapToPath.Add(session, path);
        }

        public void RemovePublisher(IStreamSession session)
        {
            if (_sessionMapToPath.TryGetValue(session, out var path))
            {
                _sessionMapToPath.Remove(session);
                _pathMapToSession.Remove(path);
            }
        }

        public IStreamSession FindPublisher(string path)
        {
            if (_pathMapToSession.TryGetValue(path, out var session))
            {
                return session;
            }
            return null;
        }

    }
}