using RtmpSharp.Messaging.Events;
using RtmpSharp.Net;

namespace RtmpSharp.Controller
{
    public abstract class AbstractController
    {
        internal IStreamSession Session { get; set; } = null;
        internal virtual void OnVideo(VideoData data) { }
        internal virtual void OnAudio(AudioData data) { }
        internal virtual void EnsureSessionStorage() {}
    }
}
