using Harmonic.NetWorking.Amf.Common;
using Harmonic.NetWorking.Amf.Serialization.Amf0;
using Harmonic.NetWorking.Amf.Serialization.Amf3;
using Harmonic.NetWorking.Rtmp;
using Harmonic.NetWorking.Rtmp.Data;
using Harmonic.NetWorking.Rtmp.Messages;
using Harmonic.NetWorking.Rtmp.Messages.Commands;
using Harmonic.NetWorking.Rtmp.Messages.UserControlMessages;
using Harmonic.NetWorking.Rtmp.Streaming;
using Harmonic.NetWorking.Utils;
using Harmonic.Rpc;
using Harmonic.Service;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Harmonic.Controllers.Record
{
    public class RecordStream : NetStream
    {
        private PublishingType _publishingType;
        private FileStream _recordFile = null;
        private RecordService _recordService = null;
        private Amf0Writer _amf0Writer = new Amf0Writer();
        private Amf3Writer _amf3Writer = new Amf3Writer();
        private Amf0Reader _amf0Reader = new Amf0Reader();
        private Amf3Reader _amf3Reader = new Amf3Reader();
        private ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
        private RtmpChunkStream VideoChunkStream { get; set; } = null;
        private RtmpChunkStream AudioChunkStream { get; set; } = null;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _recordFile?.Dispose();
            }
        }

        public RecordStream(RecordService recordService)
        {
            _recordService = recordService;
        }

        [RpcMethod(Name = "publish")]
        public void Publish([FromOptionalArgument] string streamName, [FromOptionalArgument] string publishingType)
        {
            if (string.IsNullOrEmpty(streamName))
            {
                throw new InvalidOperationException("empty publishing name");
            }
            if (!PublishingHelpers.IsTypeSupported(publishingType))
            {
                throw new InvalidOperationException($"not supported publishing type {publishingType}");
            }

            _publishingType = PublishingHelpers.PublishingTypes[publishingType];

            RtmpSession.SendControlMessageAsync(new StreamIsRecordedMessage() { StreamID = MessageStream.MessageStreamId });
            RtmpSession.SendControlMessageAsync(new StreamBeginMessage() { StreamID = MessageStream.MessageStreamId });
            var onStatus = RtmpSession.CreateCommandMessage<OnStatusCommandMessage>();
            MessageStream.RegisterMessageHandler<DataMessage>(SaveMessage);
            MessageStream.RegisterMessageHandler<AudioMessage>(SaveMessage);
            MessageStream.RegisterMessageHandler<VideoMessage>(SaveMessage);
            onStatus.InfoObject = new AmfObject
            {
                {"level", "status" },
                {"code", "NetStream.Publish.Start" },
                {"description", "Stream is now published." },
                {"details", streamName }
            };
            MessageStream.SendMessageAsync(ChunkStream, onStatus);

            _recordFile = new FileStream(_recordService.GetRecordFilename(streamName), FileMode.OpenOrCreate);
        }

        [RpcMethod("play")]
        public async Task Play(
     [FromOptionalArgument] string streamName,
     [FromOptionalArgument] double start = -1,
     [FromOptionalArgument] double duration = -1,
     [FromOptionalArgument] bool reset = false)
        {
            _recordFile = new FileStream(_recordService.GetRecordFilename(streamName), FileMode.Open, FileAccess.Read);

            var resetData = new AmfObject
            {
                {"level", "status" },
                {"code", "NetStream.Play.Reset" },
                {"description", "Resetting and playing stream." },
                {"details", streamName }
            };
            var resetStatus = RtmpSession.CreateCommandMessage<OnStatusCommandMessage>();
            resetStatus.InfoObject = resetData;
            await MessageStream.SendMessageAsync(ChunkStream, resetStatus);

            var startData = new AmfObject
            {
                {"level", "status" },
                {"code", "NetStream.Play.Start" },
                {"description", "Started playing." },
                {"details", streamName }
            };
            var startStatus = RtmpSession.CreateCommandMessage<OnStatusCommandMessage>();
            startStatus.InfoObject = startData;
            await MessageStream.SendMessageAsync(ChunkStream, startStatus);

            VideoChunkStream = RtmpSession.CreateChunkStream();
            AudioChunkStream = RtmpSession.CreateChunkStream();

            while (_recordFile.Position < _recordFile.Length)
            {
                await PlayRecordFile();
            }
        }

        private async Task PlayRecordFile()
        {
            byte[] headerBuffer = null;
            byte[] bodyBuffer = null;
            
            try
            {
                headerBuffer = _arrayPool.Rent(9);

                var bytesRead = await _recordFile.ReadAsync(headerBuffer.AsMemory(0, 9));
                if (bytesRead != 9)
                {
                    throw new InvalidDataException();
                }
                var length = (int)NetworkBitConverter.ToUInt32(headerBuffer.AsSpan(0, 4));
                var type = (MessageType)headerBuffer[4];
                var timestamp = NetworkBitConverter.ToUInt32(headerBuffer.AsSpan(5, 4));

                bodyBuffer = _arrayPool.Rent(length);
                bytesRead = await _recordFile.ReadAsync(bodyBuffer.AsMemory(0, length));
                if (bytesRead != length)
                {
                    throw new InvalidDataException();
                }
                if (RtmpSession.IOPipeline.Options.MessageFactories.TryGetValue(type, out var factory))
                {
                    var context = new NetWorking.Rtmp.Serialization.SerializationContext()
                    {
                        Amf0Reader = _amf0Reader,
                        Amf0Writer = _amf0Writer,
                        Amf3Reader = _amf3Reader,
                        Amf3Writer = _amf3Writer,
                        ReadBuffer = bodyBuffer.AsMemory(0, length)
                    };

                    var messageHeader = new MessageHeader();
                    messageHeader.MessageLength = (uint)length;
                    messageHeader.MessageType = type;
                    messageHeader.Timestamp = timestamp;
                    var message = factory(messageHeader, context, out var factoryConsumed);
                    message.MessageHeader = messageHeader;
                    context.ReadBuffer = context.ReadBuffer.Slice(factoryConsumed);
                    message.Deserialize(context);
                    context.Amf0Reader.ResetReference();
                    context.Amf3Reader.ResetReference();
                    if (message is AudioMessage)
                    {
                        await MessageStream.SendMessageAsync(AudioChunkStream, message);
                    }
                    else if (message is VideoMessage)
                    {
                        await MessageStream.SendMessageAsync(VideoChunkStream, message);
                    }
                    else
                    {
                        await MessageStream.SendMessageAsync(ChunkStream, message);
                    }
                }
            }
            finally
            {
                if (headerBuffer != null)
                {
                    _arrayPool.Return(headerBuffer);
                }
                if (bodyBuffer != null)
                {
                    _arrayPool.Return(bodyBuffer);
                }
            }

        }

        private async void SaveMessage(Message message)
        {
            var writeBuffer = new Buffers.ByteBuffer();

            byte[] buffer = null;
            byte[] messageBuffer = null;

            try
            {
                buffer = _arrayPool.Rent(4);
                NetworkBitConverter.TryGetBytes(message.MessageHeader.MessageLength, buffer);
                writeBuffer.WriteToBuffer(buffer.AsSpan(0, 4));
                writeBuffer.WriteToBuffer((byte)message.MessageHeader.MessageType);
                NetworkBitConverter.TryGetBytes(message.MessageHeader.Timestamp, buffer);
                writeBuffer.WriteToBuffer(buffer.AsSpan(0, 4));
                var context = new NetWorking.Rtmp.Serialization.SerializationContext()
                {
                    Amf0Writer = _amf0Writer,
                    Amf3Writer = _amf3Writer,
                    WriteBuffer = writeBuffer,
                };
                message.Serialize(context);
                var messageLen = writeBuffer.Length;
                messageBuffer = _arrayPool.Rent(messageLen);
                writeBuffer.TakeOutMemory(messageBuffer);

                await _recordFile.WriteAsync(messageBuffer.AsMemory(0, messageLen));
            }
            catch
            {
                RtmpSession.Close();
            }
            finally
            {
                writeBuffer.Dispose();
                if (buffer != null)
                {
                    _arrayPool.Return(buffer);
                }
                if (messageBuffer != null)
                {
                    _arrayPool.Return(messageBuffer);
                }
            }
        }
    }
}
