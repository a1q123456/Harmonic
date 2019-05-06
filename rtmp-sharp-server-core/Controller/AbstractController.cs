using RtmpSharp.Messaging.Events;
using RtmpSharp.Net;

namespace RtmpSharp.Controller
{
    public abstract class AbstractController
    {
        protected IStreamSession Session { get; set; } = null;
        public virtual void OnVideo(VideoData data) { }
        public virtual void OnAudio(AudioData data) { }
    }
}
