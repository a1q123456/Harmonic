using Harmonic.Networking.Amf.Common;
using Harmonic.Networking.Rtmp;
using Harmonic.Networking.Rtmp.Messages;
using Harmonic.Networking.Rtmp.Messages.Commands;
using Harmonic.Networking.Rtmp.Messages.UserControlMessages;
using Harmonic.Rpc;
using Harmonic.Service;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Controllers.Living
{
    public enum PublishingType
    {
        Live,
        Record,
        Append
    }

    public class LivingStream : NetStream
    {
        private PublishingType PublishingType { get; set; }
        private PublisherSessionService _publisherSessionService = null;
        private AudioMessage AACConfigureRecord = null;
        private VideoMessage AVCConfigureRecord = null;

        public LivingStream(PublisherSessionService publisherSessionService)
        {
            _publisherSessionService = publisherSessionService;
        }

        [RpcMethod("play")]
        public void Play(
            [FromOptionalArgument] string streamName,
            [FromOptionalArgument] double start = -1,
            [FromOptionalArgument] double duration = -1,
            [FromOptionalArgument] bool reset = false)
        {

        }

        [RpcMethod(Name = "publish")]
        public void Publish([FromOptionalArgument] string publishingName, [FromOptionalArgument] string publishingType)
        {
            var publishingTypeMap = new Dictionary<string, PublishingType>()
            {
                { "live", PublishingType.Live },
                { "record", PublishingType.Record },
                { "append", PublishingType.Append }
            };
            if (string.IsNullOrEmpty(publishingName))
            {
                throw new InvalidOperationException("empty publishing name");
            }
            if (!publishingTypeMap.ContainsKey(publishingType))
            {
                throw new InvalidOperationException($"not supported publishing type {publishingType}");
            }

            PublishingType = publishingTypeMap[publishingType];

            _publisherSessionService.RegisterPublisher(publishingName, this);

            RtmpSession.SendControlMessageAsync(new StreamBeginMessage() { StreamID = MessageStream.MessageStreamId });
            var onStatus = RtmpSession.CreateCommandMessage<OnStatusCommandMessage>();
            MessageStream.RegisterMessageHandler<AudioMessage>(OnAudio);
            MessageStream.RegisterMessageHandler<VideoMessage>(OnVideo);
            onStatus.InfoObject = new AmfObject
            {
                {"level", "status" },
                {"code", "NetStream.Publish.Start" },
                {"description", "Stream is now published." },
                {"details", publishingName }
            };
            MessageStream.SendMessageAsync(ChunkStream, onStatus);
        }

        public void OnAudio(AudioMessage audioData)
        {
            if (AACConfigureRecord == null && audioData.Data.Length >= 2)
            {
                AACConfigureRecord = audioData;
                return;
            }
        }

        public void OnVideo(VideoMessage videoData)
        {
            if (AVCConfigureRecord == null && videoData.Data.Length >= 2)
            {
                AVCConfigureRecord = videoData;
                return;
            }
        }

        #region Disposable Support

        private bool disposedValue = false;

        protected override void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                base.Dispose(disposing);
                _publisherSessionService.RemovePublisher(this);
                disposedValue = true;
            }
        }
        #endregion
    }
}
