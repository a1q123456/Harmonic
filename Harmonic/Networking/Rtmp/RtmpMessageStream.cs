using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Messages;
using Harmonic.Networking.Rtmp.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Harmonic.Networking.Rtmp;

public class RtmpMessageStream : IDisposable
{ 
    public uint MessageStreamId { get; private set; }
    internal RtmpSession RtmpSession { get; }
    private Dictionary<MessageType, Action<Message>> _messageHandlers = new Dictionary<MessageType, Action<Message>>();

    internal RtmpMessageStream(RtmpSession rtmpSession, uint messageStreamId)
    {
        MessageStreamId = messageStreamId;
        RtmpSession = rtmpSession;
    }

    internal RtmpMessageStream(RtmpSession rtmpSession)
    {
        MessageStreamId = rtmpSession.MakeUniqueMessageStreamId();
        RtmpSession = rtmpSession;
    }
        
    private void AttachMessage(Message message)
    {
        message.MessageHeader.MessageStreamId = MessageStreamId;
    }

    public virtual Task SendMessageAsync(RtmpChunkStream chunkStream, Message message)
    {
        AttachMessage(message);
        return RtmpSession.SendMessageAsync(chunkStream.ChunkStreamId, message);
    }

    internal void RegisterMessageHandler<T>(Action<T> handler) where T: Message
    {
        var attr = typeof(T).GetCustomAttribute<RtmpMessageAttribute>();
        if (attr == null || !attr.MessageTypes.Any())
        {
            throw new InvalidOperationException("unsupported message type");
        }
        foreach (var messageType in attr.MessageTypes)
        {
            if (_messageHandlers.ContainsKey(messageType))
            {
                throw new InvalidOperationException("message type already registered");
            }
            _messageHandlers[messageType] = m =>
            {
                handler(m as T);
            };
        }
    }

    protected void RemoveMessageHandler(MessageType messageType)
    {
        _messageHandlers.Remove(messageType);
    }

    internal void MessageArrived(Message message)
    {
        if (_messageHandlers.TryGetValue(message.MessageHeader.MessageType, out var handler))
        {
            handler(message);
        }
    }

    #region IDisposable Support
    private bool disposedValue = false;

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                RtmpSession.MessageStreamDestroying(this);
            }

            disposedValue = true;
        }
    }

    // ~RtmpMessageStream() {
    //   Dispose(false);
    // }

    public void Dispose()
    {
        Dispose(true);
        // GC.SuppressFinalize(this);
    }
    #endregion
}