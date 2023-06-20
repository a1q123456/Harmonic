using Harmonic.Networking.Amf.Common;
using Harmonic.Networking.Rtmp;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Messages;
using Harmonic.Networking.Rtmp.Messages.Commands;
using Harmonic.Networking.Rtmp.Messages.UserControlMessages;
using Harmonic.Networking.Rtmp.Streaming;
using Harmonic.Rpc;
using Harmonic.Service;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Harmonic.Controllers.Living;

public class LivingStream : NetStream
{
    private readonly List<Action> _cleanupActions = new();
    private PublishingType _publishingType;
    private readonly PublisherSessionService _publisherSessionService;
    public DataMessage? _flvMetadata;
    public AudioMessage? _aacConfigureRecord;
    public VideoMessage? _avcConfigureRecord;
    public event Action<VideoMessage> OnVideoMessage;
    public event Action<AudioMessage> OnAudioMessage;
    private RtmpChunkStream _videoChunkStream;
    private RtmpChunkStream _audioChunkStream;

    public LivingStream(PublisherSessionService publisherSessionService)
    {
        _publisherSessionService = publisherSessionService;
    }

    [RpcMethod("play")]
    public async Task Play(
        [FromOptionalArgument] string? streamName,
        [FromOptionalArgument] double start = -1,
        [FromOptionalArgument] double duration = -1,
        [FromOptionalArgument] bool reset = false)
    {
        var publisher = _publisherSessionService.FindPublisher(streamName);
        if (publisher == null)
        {
            throw new KeyNotFoundException();
        }
        var resetData = new AmfObject
        {
            {"level", "status" },
            {"code", "NetStream.Play.Reset" },
            {"description", "Resetting and playing stream." },
            {"details", streamName }
        };
        var resetStatus = this.RtmpSession.CreateCommandMessage<OnStatusCommandMessage>();
        resetStatus.InfoObject = resetData;
        await this.MessageStream.SendMessageAsync(this.ChunkStream, resetStatus);

        var startData = new AmfObject
        {
            {"level", "status" },
            {"code", "NetStream.Play.Start" },
            {"description", "Started playing." },
            {"details", streamName }
        };
        var startStatus = this.RtmpSession.CreateCommandMessage<OnStatusCommandMessage>();
        startStatus.InfoObject = startData;
        await this.MessageStream.SendMessageAsync(this.ChunkStream, startStatus);

        var flvMetadata = this.RtmpSession.CreateData<DataMessage>();
        flvMetadata.MessageHeader = (MessageHeader)publisher._flvMetadata.MessageHeader.Clone();
        flvMetadata.Data = publisher._flvMetadata.Data;
        await this.MessageStream.SendMessageAsync(this.ChunkStream, flvMetadata);

        _videoChunkStream = this.RtmpSession.CreateChunkStream();
        _audioChunkStream = this.RtmpSession.CreateChunkStream();

        await this.MessageStream.SendMessageAsync(_audioChunkStream, publisher._aacConfigureRecord.Clone() as AudioMessage);
        if (publisher._avcConfigureRecord is null)
        {
            await this.MessageStream.SendMessageAsync(_videoChunkStream, publisher._avcConfigureRecord.Clone() as VideoMessage);
        }

        publisher.OnAudioMessage += SendAudio;
        publisher.OnVideoMessage += SendVideo;
        _cleanupActions.Add(() =>
        {
            publisher.OnVideoMessage -= SendVideo;
            publisher.OnAudioMessage -= SendAudio;
        });
    }

    private async void SendVideo(VideoMessage message)
    {
        var video = message.Clone() as VideoMessage;
            
        try
        {
            await this.MessageStream.SendMessageAsync(_videoChunkStream, video);
        }
        catch
        {
            foreach (var a in _cleanupActions)
            {
                a();
            }

            this.RtmpSession.Close();
        }
    }

    private async void SendAudio(AudioMessage message)
    {
        var audio = message.Clone();
        try
        {
            await this.MessageStream.SendMessageAsync(_audioChunkStream, audio as AudioMessage);
        }
        catch
        {
            foreach (var a in _cleanupActions)
            {
                a();
            }

            this.RtmpSession.Close();
        }
    }

    [RpcMethod(Name = "publish")]
    public void Publish([FromOptionalArgument] string? publishingName, [FromOptionalArgument] string publishingType)
    {
        if (string.IsNullOrEmpty(publishingName))
        {
            throw new InvalidOperationException("empty publishing name");
        }
        if (!PublishingHelpers.IsTypeSupported(publishingType))
        {
            throw new InvalidOperationException($"not supported publishing type {publishingType}");
        }

        _publishingType = PublishingHelpers.PublishingTypes[publishingType];

        _publisherSessionService.RegisterPublisher(publishingName, this);

        this.RtmpSession.SendControlMessageAsync(new StreamBeginMessage() { StreamId = this.MessageStream.MessageStreamId });
        var onStatus = this.RtmpSession.CreateCommandMessage<OnStatusCommandMessage>();
        this.MessageStream.RegisterMessageHandler<DataMessage>(HandleDataMessage);
        this.MessageStream.RegisterMessageHandler<AudioMessage>(HandleAudioMessage);
        this.MessageStream.RegisterMessageHandler<VideoMessage>(HandleVideoMessage);
        onStatus.InfoObject = new AmfObject
        {
            {"level", "status" },
            {"code", "NetStream.Publish.Start" },
            {"description", "Stream is now published." },
            {"details", publishingName }
        };
        this.MessageStream.SendMessageAsync(this.ChunkStream, onStatus);
    }

    private void HandleDataMessage(DataMessage msg)
    {
        _flvMetadata = msg;
    }

    private void HandleAudioMessage(AudioMessage audioData)
    {
        if (_aacConfigureRecord == null && audioData.Data.Length >= 2)
        {
            _aacConfigureRecord = audioData;
            return;
        }
        OnAudioMessage.Invoke(audioData);
    }

    private void HandleVideoMessage(VideoMessage videoData)
    {
        if (_avcConfigureRecord == null && videoData.Data.Length >= 2)
        {
            _avcConfigureRecord = videoData;
        }
        OnVideoMessage(videoData);
    }

    #region Disposable Support

    private bool _disposedValue;

    protected override void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            base.Dispose(disposing);
            _publisherSessionService.RemovePublisher(this);
            _videoChunkStream?.Dispose();
            _audioChunkStream?.Dispose();

            _disposedValue = true;
        }
    }
    #endregion
}