using Harmonic.NetWorking.Rtmp.Data;
using Harmonic.NetWorking.Rtmp.Serialization;
using Harmonic.NetWorking.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Messages.UserControlMessages
{
    public class UserControlMessageFactory
    {
        public Dictionary<UserControlEventType, Type> _messageFactories = new Dictionary<UserControlEventType, Type>();

        public void RegisterMessage<T>() where T: UserControlMessage, new()
        {
            var tType = typeof(T);
            var attr = tType.GetCustomAttribute<UserControlMessageAttribute>();
            if (attr == null)
            {
                throw new InvalidOperationException();
            }
            _messageFactories.Add(attr.Type, tType);
        }

        public Message Provide(MessageHeader header, SerializationContext context, out int consumed)
        {
            var type = (UserControlEventType)NetworkBitConverter.ToUInt16(context.ReadBuffer.Span);
            if (!_messageFactories.TryGetValue(type, out var t))
            {
                throw new NotSupportedException();
            }
            consumed = 0;
            return (Message)Activator.CreateInstance(t);
        }
    }
}
