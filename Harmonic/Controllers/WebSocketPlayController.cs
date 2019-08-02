using Harmonic.Networking;
using Harmonic.Networking.Rtmp.Messages;
using Harmonic.Service;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Harmonic.Controllers
{
    public class WebSocketPlayController : WebSocketController, IDisposable
    {
        class SeekAction
        {
            [JsonProperty("action")]
            public string Action { get; set; }
            [JsonProperty("filePos")]
            public double FilePos { get; set; }
        }

        private RecordService _recordService = null;
        private PublisherSessionService _publisherSessionService = null;
        private List<Action> _cleanupActions = new List<Action>();
        private FileStream _recordFile = null;
        private SemaphoreSlim _playLock = new SemaphoreSlim(1);
        private int _playing = 0;
        private long _playRangeTo = 0;

        public WebSocketPlayController(PublisherSessionService publisherSessionService, RecordService recordService)
        {
            _publisherSessionService = publisherSessionService;
            _recordService = recordService;
        }

        public override async Task OnConnect()
        {
            var publisher = _publisherSessionService.FindPublisher(StreamName);
            if (publisher != null)
            {
                _cleanupActions.Add(() =>
                {
                    publisher.OnAudioMessage -= SendAudio;
                    publisher.OnVideoMessage -= SendVideo;
                });

                var metadata = (Dictionary<string, object>)publisher.FlvMetadata.Data.Last();
                var hasAudio = metadata.ContainsKey("audiocodecid");
                var hasVideo = metadata.ContainsKey("videocodecid");

                await Session.SendFlvHeaderAsync(hasAudio, hasVideo);

                await Session.SendMessageAsync(publisher.FlvMetadata);
                if (hasAudio)
                {
                    await Session.SendMessageAsync(publisher.AACConfigureRecord);
                }
                if (hasVideo)
                {
                    await Session.SendMessageAsync(publisher.AVCConfigureRecord);
                }

                publisher.OnAudioMessage += SendAudio;
                publisher.OnVideoMessage += SendVideo;
            }
            // play record
            else
            {
                _recordFile = new FileStream(_recordService.GetRecordFilename(StreamName) + ".flv", FileMode.Open, FileAccess.Read);
                var fromStr = Query.Get("from");
                long from = 0;
                if (fromStr != null)
                {
                    from = long.Parse(fromStr);
                }
                var toStr = Query.Get("to");
                _playRangeTo = -1;
                if (toStr != null)
                {
                    _playRangeTo = long.Parse(toStr);
                }

                var header = new byte[9];

                await _recordFile.ReadBytesAsync(header);
                await Session.SendRawDataAsync(header);

                from = Math.Max(from, 9);

                _recordFile.Seek(from, SeekOrigin.Begin);

                await PlayRecordFile();
            }
        }

        private async Task PlayRecordFile()
        {
            Interlocked.Exchange(ref _playing, 1);
            var buffer = new byte[512];
            int bytesRead = 0;
            do
            {
                await _playLock.WaitAsync();
                bytesRead = await _recordFile.ReadAsync(buffer);
                await Session.SendRawDataAsync(buffer);
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
            Session.SendMessageAsync(message);
        }

        private void SendAudio(AudioMessage message)
        {
            Session.SendMessageAsync(message);
        }

        public override async void OnMessage(string msg)
        {
            //var obj = JObject.Parse(msg);
            //var action = obj["action"].Value<string>();

            //if (action == "suspend")
            //{
            //    await _playLock.WaitAsync();
            //}
            //else if (action == "seek")
            //{
            //    if (_playLock.CurrentCount != 0)
            //    {
            //        return;
            //    }

            //    var pos = JsonConvert.DeserializeObject<SeekAction>(msg).FilePos;

            //    _recordFile.Seek((int)pos, SeekOrigin.Begin);

            //    _playLock.Release();

            //    if (_playing == 0)
            //    {
            //        await PlayRecordFile();
            //    }

            //}
        }

        #region IDisposable Support
        private bool disposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    foreach (var c in _cleanupActions)
                    {
                        c();
                    }
                    _recordFile?.Dispose();
                }

                disposedValue = true;
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
}
