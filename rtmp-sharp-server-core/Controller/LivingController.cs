using System.Collections.Generic;
using RtmpSharp.Messaging.Events;

namespace RtmpSharp.Controller
{
    public class LivingController : AbstractController
    {
        public LivingController()
        {
        }

        public override void OnAudio(AudioData audioData)
        {
            var sessionStorage = Session.SessionStorage;
            if (Session.AACConfigureRecord != null && audioData.Data.Length >= 2 && audioData.Data[1] == 0)
            {
                Session.AACConfigureRecord = audioData;
                return;
            }
            Session.AudioBuffer.Enqueue(audioData);
        }

        public override void OnVideo(VideoData videoData)
        {
            var sessionStorage = Session.SessionStorage;
            if (Session.AVCConfigureRecord != null && videoData.Data.Length >= 2 && videoData.Data[1] == 0)
            {
                Session.AVCConfigureRecord = videoData;
                return;
            }
            Session.VideoBuffer.Enqueue(videoData);
        }
    }
}