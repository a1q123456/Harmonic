using Harmonic.Controllers;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Messages;
using Harmonic.Networking.Rtmp.Messages.Commands;
using Harmonic.Networking.Rtmp.Messages.UserControlMessages;
using Harmonic.Networking.Rtmp.Serialization;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Harmonic.Networking.Rtmp
{
    class RtmpServerOptions
    {
        internal Dictionary<MessageType, MessageFactory> _messageFactories = new Dictionary<MessageType, MessageFactory>();
        public delegate Message MessageFactory(MessageHeader header, Serialization.SerializationContext context);
        private Dictionary<string, Type> _registeredControllers = new Dictionary<string, Type>();
        public IReadOnlyDictionary<string, Type> RegisteredControllers => _registeredControllers;

        public RtmpServerOptions()
        {
            var userControlMessageFactory = new UserControlMessageFactory();
            var commandMessageFactory = new CommandMessageFactory();
            RegisterMessage<AbortMessage>();
            RegisterMessage<AcknowledgementMessage>();
            RegisterMessage<SetChunkSizeMessage>();
            RegisterMessage<SetPeerBandwidthMessage>();
            RegisterMessage<WindowAcknowledgementSizeMessage>();
            RegisterMessage<DataMessage>();
            RegisterMessage<UserControlMessage>(userControlMessageFactory.Provide);
            RegisterMessage<CommandMessage>(commandMessageFactory.Provide);
        }

        private void RegisterMessage<T>(MessageFactory factory) where T : Message
        {
            var tType = typeof(T);
            var attr = tType.GetCustomAttribute<RtmpMessageAttribute>();
            if (attr == null)
            {
                throw new InvalidOperationException();
            }

            foreach (var messageType in attr.MessageTypes)
            {
                _messageFactories.Add(messageType, factory);
            }
        }

        private void RegisterMessage<T>() where T : Message, new()
        {
            var tType = typeof(T);
            var attr = tType.GetCustomAttribute<RtmpMessageAttribute>();
            if (attr == null)
            {
                throw new InvalidOperationException();
            }

            foreach (var messageType in attr.MessageTypes)
            {
                _messageFactories.Add(messageType, (a, b) => new T());
            }
        }

        public void RegisterController<T>(string appName = null) where T: AbstractController
        {
            var tType = typeof(T);
            var name = appName ?? tType.Name.Replace("Controller", "");
            _registeredControllers.Add(appName, tType);
        }
    }
}
