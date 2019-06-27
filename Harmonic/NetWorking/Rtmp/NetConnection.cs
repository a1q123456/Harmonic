using Harmonic.NetWorking.Rtmp.Data;
using Harmonic.NetWorking.Rtmp.Messages;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Harmonic.NetWorking.Rtmp
{
    public class NetConnection : IDisposable
    {
        private RtmpSession _rtmpSession = null;
        private RtmpChunkStream _rtmpChunkStream = null;
        private Dictionary<uint, NetStream> _netStreams = new Dictionary<uint, NetStream>();
        private RtmpControlMessageStream _controlMessageStream = null;
        public IReadOnlyDictionary<uint, NetStream> NetStreams { get => _netStreams; }

        internal NetConnection(RtmpSession rtmpSession)
        {
            _rtmpSession = rtmpSession;
            _rtmpChunkStream = _rtmpSession.CreateChunkStream();
            _controlMessageStream = _rtmpSession.ControlMessageStream;
            _controlMessageStream.RegisterMessageHandler<CommandMessage>(MessageType.Amf0Command, CommandHandler);
            _controlMessageStream.RegisterMessageHandler<CommandMessage>(MessageType.Amf3Command, CommandHandler);
        }

        public void Connect()
        {


            // TBD
        }

        public void Close()
        {
            // TBD
        }

        public uint CreateStream()
        {
            var stream = new NetStream(_rtmpSession);
            _netStreams.Add(stream.MessageStreamId, stream);
            return stream.MessageStreamId;
        }

        internal void MessageStreamDestroying(NetStream stream)
        {
            _netStreams.Remove(stream.MessageStreamId);
        }

        private void CommandHandler(CommandMessage command)
        {
            // TBD Rpc
        }

        #region IDisposable Support
        private bool disposedValue = false; // 要检测冗余调用

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    foreach ((var streamId, var stream) in _netStreams)
                    {
                        stream.Dispose();
                    }
                    _rtmpChunkStream.Dispose();
                }

                // TODO: 释放未托管的资源(未托管的对象)并在以下内容中替代终结器。
                // TODO: 将大型字段设置为 null。

                disposedValue = true;
            }
        }

        // TODO: 仅当以上 Dispose(bool disposing) 拥有用于释放未托管资源的代码时才替代终结器。
        // ~NetConnection() {
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
