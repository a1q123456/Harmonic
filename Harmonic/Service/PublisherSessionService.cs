using System;
using System.Linq;
using System.Collections.Generic;
using Harmonic.Controllers.Living;
using Harmonic.Networking.Rtmp.Messages;

namespace Harmonic.Service
{
    public class PublisherSessionService
    {
        private readonly Dictionary<string, LivingStream> _pathMapToSession = new Dictionary<string, LivingStream>();
        private readonly Dictionary<LivingStream, string> _sessionMapToPath = new Dictionary<LivingStream, string>();

        internal void RegisterPublisher(string publishingName, LivingStream session)
        {
            if (_pathMapToSession.ContainsKey(publishingName))
            {
                throw new InvalidOperationException(Resource.request_instance_is_publishing);
            }
            if (_sessionMapToPath.ContainsKey(session))
            {
                throw new InvalidOperationException(Resource.request_session_is_publishing);
            }
            _pathMapToSession.Add(publishingName, session);
            _sessionMapToPath.Add(session, publishingName);
        }

        internal void RemovePublisher(LivingStream session)
        {
            if (_sessionMapToPath.TryGetValue(session, out var publishingName))
            {
                _sessionMapToPath.Remove(session);
                _pathMapToSession.Remove(publishingName);
            }
        }
        public LivingStream FindPublisher(string publishingName)
        {
            if (_pathMapToSession.TryGetValue(publishingName, out var session))
            {
                return session;
            }
            return null;
        }

    }
}