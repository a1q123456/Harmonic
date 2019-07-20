using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Messages;
using Harmonic.Networking.Rtmp.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Harmonic.Networking.Rtmp
{
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
        private bool disposedValue = false; // 要检测冗余调用

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    RtmpSession.MessageStreamDestroying(this);
                }

                // TODO: 释放未托管的资源(未托管的对象)并在以下内容中替代终结器。
                // TODO: 将大型字段设置为 null。

                disposedValue = true;
            }
        }

        // TODO: 仅当以上 Dispose(bool disposing) 拥有用于释放未托管资源的代码时才替代终结器。
        // ~RtmpMessageStream() {
        //   // 请勿更改此代码。将清理代码放入以上 Dispose(bool disposing) 中。
        //   Dispose(false);
        // }

        // 添加此代码以正确实现可处置模式。
        public void Dispose()
        {
            // 请勿更改此代码。将清理代码放入以上 Dispose(bool disposing) 中。
            Dispose(true);
            // TODO: 如果在以上内容中替代了终结器，则取消注释以下行。
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
