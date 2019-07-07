using Harmonic.Controllers;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Messages;
using Harmonic.Networking.Rtmp.Messages.Commands;
using Harmonic.Rpc;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Harmonic.Networking.Rtmp
{
    public class RtmpSession : IDisposable
    {
        internal IOPipeLine IOPipeline { get; set; } = null;
        private Dictionary<uint, RtmpMessageStream> _messageStreams = new Dictionary<uint, RtmpMessageStream>();
        private Random _random = new Random();
        internal RtmpControlChunkStream ControlChunkStream { get; }
        public RtmpControlMessageStream ControlMessageStream { get; }
        public NetConnection NetConnection { get; }
        private RpcService _rpcService = null;

        internal RtmpSession(IOPipeLine ioPipeline)
        {
            IOPipeline = ioPipeline;
            ControlChunkStream = new RtmpControlChunkStream(this);
            ControlMessageStream = new RtmpControlMessageStream(this);
            NetConnection = new NetConnection(this);
            ControlMessageStream.RegisterMessageHandler<SetChunkSizeMessage>(MessageType.SetChunkSize, SetChunkSize);
            ControlMessageStream.RegisterMessageHandler<WindowAcknowledgementSizeMessage>(MessageType.WindowAcknowledgementSize, WindowAcknowledgementSize);
            ControlMessageStream.RegisterMessageHandler<SetPeerBandwidthMessage>(MessageType.SetPeerBandwidth, SetPeerBandwidth);
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

        public T CreateNetStream<T>() where T: AbstractController, new()
        {
            var ret = new T();
            ret.MessageStream = CreateMessageStream();
            ret.MessageStream.RegisterMessageHandler<CommandMessage>(MessageType.Amf0Command, c => CommandHandler(ret, c));
            ret.MessageStream.RegisterMessageHandler<CommandMessage>(MessageType.Amf3Command, c => CommandHandler(ret, c));
            ret.ChunkStream = CreateChunkStream();
            NetConnection._netStreams.Add(ret.MessageStream.MessageStreamId, ret);
            return ret;
        }

        internal void CommandHandler(AbstractController controller, CommandMessage command)
        {
            object result = null;
            try
            {
                result = _rpcService.InvokeMethod(controller, command);
            }
            catch (Exception e)
            {
                var retCommand = new ReturnResultCommandMessage(command.AmfEncodingVersion);
                retCommand.ProcedureName = "_error";
                retCommand.TranscationID = command.TranscationID;
                retCommand.CommandObject = null;
                retCommand.ReturnValue = e.Message;
                _ = SendMessageAsync(controller.ChunkStream.ChunkStreamId, retCommand);
                return;
            }
            if (result != null)
            {
                var resType = result.GetType();
                if (resType.IsGenericType && resType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    var tsk = result as Task;
                    tsk.ContinueWith(t =>
                    {
                        var taskResult = resType.GetProperty("Result").GetValue(result);
                        var retCommand = new ReturnResultCommandMessage(command.AmfEncodingVersion);
                        retCommand.ProcedureName = "_result";
                        retCommand.TranscationID = command.TranscationID;
                        retCommand.CommandObject = null;
                        retCommand.ReturnValue = taskResult;
                        _ = SendMessageAsync(controller.ChunkStream.ChunkStreamId, retCommand);
                    }, TaskContinuationOptions.OnlyOnRanToCompletion);
                    tsk.ContinueWith(t =>
                    {
                        var exception = tsk.Exception;
                        var retCommand = new ReturnResultCommandMessage(command.AmfEncodingVersion);
                        retCommand.ProcedureName = "_error";
                        retCommand.TranscationID = command.TranscationID;
                        retCommand.CommandObject = null;
                        retCommand.ReturnValue = exception.Message;
                        _ = SendMessageAsync(controller.ChunkStream.ChunkStreamId, retCommand);
                    }, TaskContinuationOptions.OnlyOnFaulted);
                }
                else if (resType != typeof(void))
                {
                    var taskResult = resType.GetProperty("Result").GetValue(result);
                    var retCommand = new ReturnResultCommandMessage(command.AmfEncodingVersion);
                    retCommand.ProcedureName = "_result";
                    retCommand.TranscationID = command.TranscationID;
                    retCommand.CommandObject = null;
                    retCommand.ReturnValue = taskResult;
                    _ = SendMessageAsync(controller.ChunkStream.ChunkStreamId, retCommand);
                }
            }
        }

        internal bool FindController(string appName, out Type controllerType)
        {
            return IOPipeline._options.RegisteredControllers.TryGetValue(appName, out controllerType);
        }

        public void Close()
        {
            IOPipeline.Disconnect();
        }

        private RtmpMessageStream CreateMessageStream()
        {
            var stream = new RtmpMessageStream(this);
            MessageStreamCreated(stream);
            return stream;
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
            return IOPipeline.MultiplexMessageAsync(chunkStreamId, message);
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

        private void SetPeerBandwidth(SetPeerBandwidthMessage message)
        {
            IOPipeline.ChunkStreamContext.ReadWindowAcknowledgementSize = message.WindowSize;
            SendControlMessageAsync(new AcknowledgementMessage()
            {
                BytesReceived = IOPipeline.ChunkStreamContext.ReadWindowSize
            });
            IOPipeline.ChunkStreamContext.ReadWindowSize = 0;
            IOPipeline.ChunkStreamContext.BandwidthLimited = true;
        }

        private void WindowAcknowledgementSize(WindowAcknowledgementSizeMessage message)
        {
            IOPipeline.ChunkStreamContext.ReadWindowAcknowledgementSize = message.WindowSize;
            SendControlMessageAsync(new AcknowledgementMessage()
            {
                BytesReceived = IOPipeline.ChunkStreamContext.ReadWindowSize
            });
            IOPipeline.ChunkStreamContext.ReadWindowSize = 0;
        }

        private void SetChunkSize(SetChunkSizeMessage setChunkSize)
        {
            IOPipeline.ChunkStreamContext.ReadChunkSize = (int)setChunkSize.ChunkSize;
        }

        public Task SendControlMessageAsync(Message message)
        {
            if (message.MessageHeader.MessageType == MessageType.WindowAcknowledgementSize)
            {
                IOPipeline.ChunkStreamContext.WriteWindowAcknowledgementSize = ((WindowAcknowledgementSizeMessage)message).WindowSize;
            }
            return SendMessageAsync(ControlChunkStream.ChunkStreamId, message);
        }

        #region IDisposable Support
        private bool disposedValue = false; // 要检测冗余调用

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    NetConnection.Dispose();
                    ControlChunkStream.Dispose();
                    ControlMessageStream.Dispose();
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
