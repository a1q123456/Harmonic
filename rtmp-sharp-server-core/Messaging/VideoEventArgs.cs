using System;
using RtmpSharp.Messaging.Events;

namespace RtmpSharp.Messaging
{
    public class VideoEventArgs : EventArgs
    {
        public VideoData VideoData { get; private set; }

        public VideoEventArgs(VideoData videoData)
        {
            VideoData = videoData;
        }
    }
}