
namespace RtmpSharp.Controller
{
    public abstract class AbstractController
    {
        public abstract void OnVideo(byte[] data);
        public abstract void OnAudio(byte[] data);
    }
}
