using Harmonic.Networking;
using Harmonic.Networking.Rtmp.Messages;
using Harmonic.Service;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Harmonic.Controllers;

public class WebSocketPlayController : WebSocketController, IDisposable
{
    private readonly RecordService _recordService;
    private readonly PublisherSessionService _publisherSessionService;
    private readonly List<Action> _cleanupActions = new();
    private FileStream _recordFile;
    private readonly SemaphoreSlim _playLock = new(1);
    private int _playing;
    private long _playRangeTo;

    public WebSocketPlayController(PublisherSessionService publisherSessionService, RecordService recordService)
    {
        _publisherSessionService = publisherSessionService;
        _recordService = recordService;
    }

    public override async Task OnConnect()
    {
        var publisher = _publisherSessionService.FindPublisher(this.StreamName);
        if (publisher != null)
        {
            _cleanupActions.Add(() =>
            {
                publisher.OnAudioMessage -= SendAudio;
                publisher.OnVideoMessage -= SendVideo;
            });

            var metadata = (Dictionary<string, object>)publisher._flvMetadata.Data.Last();
            var hasAudio = metadata.ContainsKey("audiocodecid");
            var hasVideo = metadata.ContainsKey("videocodecid");

            await this.Session.SendFlvHeaderAsync(hasAudio, hasVideo);

            await this.Session.SendMessageAsync(publisher._flvMetadata);
            if (hasAudio)
            {
                await this.Session.SendMessageAsync(publisher._aacConfigureRecord);
            }
            if (hasVideo)
            {
                await this.Session.SendMessageAsync(publisher._avcConfigureRecord);
            }

            publisher.OnAudioMessage += SendAudio;
            publisher.OnVideoMessage += SendVideo;
        }
        // play record
        else
        {
            _recordFile = new FileStream(_recordService.GetRecordFilename(this.StreamName) + ".flv", FileMode.Open, FileAccess.Read);
            var fromStr = this.Query.Get("from");
            long from = 0;
            if (fromStr != null)
            {
                from = long.Parse(fromStr);
            }
            var toStr = this.Query.Get("to");
            _playRangeTo = -1;
            if (toStr != null)
            {
                _playRangeTo = long.Parse(toStr);
            }

            var header = new byte[9];

            await _recordFile.ReadBytesAsync(header);
            await this.Session.SendRawDataAsync(header);

            from = Math.Max(from, 9);

            _recordFile.Seek(from, SeekOrigin.Begin);

            await PlayRecordFile();
        }
    }

    private async Task PlayRecordFile()
    {
        Interlocked.Exchange(ref _playing, 1);
        var buffer = new byte[512];
        int bytesRead;
        do
        {
            await _playLock.WaitAsync();
            bytesRead = await _recordFile.ReadAsync(buffer);
            await this.Session.SendRawDataAsync(buffer);
            _playLock.Release();
            if (_playRangeTo < _recordFile.Position && _playRangeTo != -1)
            {
                break;
            }
        } while (bytesRead != 0);
        Interlocked.Exchange(ref _playing, 0);
    }

    private void SendVideo(VideoMessage message)
    {
        this.Session.SendMessageAsync(message);
    }

    private void SendAudio(AudioMessage message)
    {
        this.Session.SendMessageAsync(message);
    }

    public override void OnMessage(string msg)
    {
    }

    #region IDisposable Support
    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                foreach (var c in _cleanupActions)
                {
                    c();
                }
                _recordFile?.Dispose();
            }

            _disposedValue = true;
        }
    }

    // ~WebSocketPlayController()
    // {
    //   Dispose(false);
    // }

    public void Dispose()
    {
        Dispose(true);
        // GC.SuppressFinalize(this);
    }
    #endregion
}