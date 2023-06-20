using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Messages.Commands;
using Harmonic.Networking.Rtmp.Serialization;
using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;

namespace Harmonic.Networking.Rtmp.Messages.UserControlMessages;

public class CommandMessageFactory
{
    public Dictionary<string, Type> _messageFactories = new();

    public CommandMessageFactory()
    {
        RegisterMessage<ConnectCommandMessage>();
        RegisterMessage<CreateStreamCommandMessage>();
        RegisterMessage<DeleteStreamCommandMessage>();
        RegisterMessage<OnStatusCommandMessage>();
        RegisterMessage<PauseCommandMessage>();
        RegisterMessage<Play2CommandMessage>();
        RegisterMessage<PlayCommandMessage>();
        RegisterMessage<PublishCommandMessage>();
        RegisterMessage<ReceiveAudioCommandMessage>();
        RegisterMessage<ReceiveVideoCommandMessage>();
        RegisterMessage<SeekCommandMessage>();

    }

    public void RegisterMessage<T>() where T: CommandMessage
    {
        var tType = typeof(T);
        var attr = tType.GetCustomAttribute<RtmpCommandAttribute>();
        if (attr == null)
        {
            throw new InvalidOperationException();
        }
        _messageFactories.Add(attr.Name, tType);
    }

    public Message Provide(MessageHeader header, SerializationContext context, out int consumed)
    {
        string name = null;
        bool amf3 = false;
        if (header.MessageType == MessageType.Amf0Command)
        {
            if (!context.Amf0Reader.TryGetString(context.ReadBuffer.Span, out name, out consumed))
            {
                throw new ProtocolViolationException();
            }
        }
        else if (header.MessageType == MessageType.Amf3Command)
        {
            amf3 = true;
            if (!context.Amf3Reader.TryGetString(context.ReadBuffer.Span, out name, out consumed))
            {
                throw new ProtocolViolationException();
            }
        }
        else
        {
            throw new InvalidOperationException();
        }
        if (!_messageFactories.TryGetValue(name, out var t))
        {
            throw new NotSupportedException();
        }
        var ret = (CommandMessage)Activator.CreateInstance(t, amf3 ? AmfEncodingVersion.Amf3 : AmfEncodingVersion.Amf0);
        ret.ProcedureName = name;
        return ret;
    }
}