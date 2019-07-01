using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Messages;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Harmonic.Networking.Rtmp
{
    class RtmpSession : IDisposable
    {
        internal RtmpStream RtmpStream { get; set; } = null;
        private Dictionary<uint, RtmpMessageStream> _messageStreams = new Dictionary<uint, RtmpMessageStream>();
        private Random _random = new Random();
        public RtmpControlChunkStream ControlChunkStream { get; }
        public RtmpControlMessageStream ControlMessageStream { get; }
        public NetConnection NetConnection { get; }

        public RtmpSession(RtmpStream rtmpStream)
        {
            RtmpStream = rtmpStream;
            ControlChunkStream = new RtmpControlChunkStream(this);
            ControlMessageStream = new RtmpControlMessageStream(this);
            NetConnection = new NetConnection(this);
        }

        internal uint MakeUniqueMessageStreamId()
        {
            // TBD use uint.MaxValue
            return (uint)_random.Next(1, int.MaxValue);
        }

        internal uint MakeUniqueChunkStreamId()
        {
            // TBD make csid unique
            return (uint)_random.Next(3, 65599);
        }

        public RtmpChunkStream CreateChunkStream()
        {
            return new RtmpChunkStream(this);
        }

        internal void ChunkStreamDestroyed(RtmpChunkStream rtmpChunkStream)
        {
            // TBD
        }

        internal Task SendMessageAsync(uint chunkStreamId, Message message)
        {
            return RtmpStream.MultiplexMessageAsync(chunkStreamId, message);
        }

        internal void MessageStreamCreated(RtmpMessageStream messageStream)
        {
            _messageStreams[messageStream.MessageStreamId] = messageStream;
        }

        internal void MessageStreamDestroying(RtmpMessageStream messageStream)
        {
            _messageStreams.Remove(messageStream.MessageStreamId);
        }

        internal void MessageArrived(Message message)
        {
            if (_messageStreams.TryGetValue(message.MessageHeader.MessageStreamId.Value, out var stream))
            {
                stream.MessageArrived(message);
            }
        }

        internal void Acknowledgement(uint bytesReceived)
        {
            _ = ControlMessageStream.SendMessageAsync(ControlChunkStream, new AcknowledgementMessage()
            {
                BytesReceived = bytesReceived
            });
        }

        #region IDisposable Support
        private bool disposedValue = false; // 要检测冗余调用

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {

                }

                // TODO: 释放未托管的资源(未托管的对象)并在以下内容中替代终结器。
                // TODO: 将大型字段设置为 null。

                disposedValue = true;
            }
        }

        // TODO: 仅当以上 Dispose(bool disposing) 拥有用于释放未托管资源的代码时才替代终结器。
        // ~RtmpSession() {
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
