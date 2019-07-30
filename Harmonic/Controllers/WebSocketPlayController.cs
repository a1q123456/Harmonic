using Harmonic.NetWorking.Rtmp.Messages;
using Harmonic.Service;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Harmonic.Controllers
{
    public class WebSocketPlayController : WebSocketController, IDisposable
    {
        private PublisherSessionService _publisherSessionService = null;
        private List<Action> _cleanupActions = new List<Action>();

        public WebSocketPlayController(PublisherSessionService publisherSessionService)
        {
            _publisherSessionService = publisherSessionService;
        }

        public override void OnConnect()
        {
            var publisher = _publisherSessionService.FindPublisher(StreamName);
            if (publisher == null)
            {
                throw new KeyNotFoundException();
            }

            _cleanupActions.Add(() =>
            {
                publisher.OnAudioMessage -= SendAudio;
                publisher.OnVideoMessage -= SendVideo;
            });

            var metadata = (Dictionary<string, object>)publisher.FlvMetadata.Data.Last();
            var hasAudio = metadata.ContainsKey("audiocodecid");
            var hasVideo = metadata.ContainsKey("videocodecid");

            Session.SendFlvHeader(hasAudio, hasVideo);

            Session.SendMessage(publisher.FlvMetadata);
            if (hasAudio)
            {
                Session.SendMessage(publisher.AACConfigureRecord);
            }
            if (hasVideo)
            {
                Session.SendMessage(publisher.AVCConfigureRecord);
            }

            publisher.OnAudioMessage += SendAudio;
            publisher.OnVideoMessage += SendVideo;
        }

        private void SendVideo(VideoMessage message)
        {
            Session.SendMessage(message);
        }

        private void SendAudio(AudioMessage message)
        {
            Session.SendMessage(message);
        }

        public override void OnMessage(string msg)
        {

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
