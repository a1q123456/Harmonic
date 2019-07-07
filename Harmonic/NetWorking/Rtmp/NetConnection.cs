using Harmonic.Controllers;
using Harmonic.Networking.Amf.Common;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Messages;
using Harmonic.Networking.Rtmp.Messages.Commands;
using Harmonic.Networking.Rtmp.Serialization;
using Harmonic.Rpc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Harmonic.Networking.Rtmp
{
    public class NetConnection : IDisposable
    {
        private RtmpSession _rtmpSession = null;
        private RtmpChunkStream _rtmpChunkStream = null;
        internal Dictionary<uint, AbstractController> _netStreams = new Dictionary<uint, AbstractController>();
        private RtmpControlMessageStream _controlMessageStream = null;
        public IReadOnlyDictionary<uint, AbstractController> NetStreams { get => _netStreams; }
        private AbstractController _controller;
        private AbstractController Controller
        {
            get
            {
                return _controller;
            }
            set
            {
                if (_controller != null)
                {
                    throw new InvalidOperationException("already have an controller");
                }

                _controller = value ?? throw new InvalidOperationException("controller cannot be null");
                _controller.MessageStream = _controlMessageStream;
                _controller.ChunkStream = _rtmpChunkStream;
            }
        }

        internal NetConnection(RtmpSession rtmpSession)
        {
            _rtmpSession = rtmpSession;
            _rtmpChunkStream = _rtmpSession.CreateChunkStream();
            _controlMessageStream = _rtmpSession.ControlMessageStream;
            _controlMessageStream.RegisterMessageHandler<CommandMessage>(MessageType.Amf0Command, CommandHandler);
            _controlMessageStream.RegisterMessageHandler<CommandMessage>(MessageType.Amf3Command, CommandHandler);
        }

        private void CommandHandler(CommandMessage command)
        {
            if (command.ProcedureName == "connect")
            {
                Connect(command);
            }
            else if (command.ProcedureName == "close")
            {
                Close();
            }
            else if (_controller != null)
            {
                _rtmpSession.CommandHandler(_controller, command);
            }
            else
            {
                _rtmpSession.Close();
            }
        }
        
        public void Connect(CommandMessage command)
        {
            var commandObj = command.CommandObject;
            if (_rtmpSession.FindController(commandObj.Fields["app"] as string, out var controllerType))
            {
                Controller = Activator.CreateInstance(controllerType) as AbstractController;
            }
            else
            {
                _rtmpSession.Close();
            }
        }

        public void Close()
        {
            _rtmpSession.Close();
        }

        internal void MessageStreamDestroying(NetStream stream)
        {
            _netStreams.Remove(stream.MessageStream.MessageStreamId);
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
                        if (stream is IDisposable disp)
                        {
                            disp.Dispose();
                        }
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
