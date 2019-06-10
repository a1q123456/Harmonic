using System;
using RtmpSharp.Messaging.Events;

namespace RtmpSharp.Net
{
    public class AudioEventArgs : EventArgs
    {
        public AudioData AudioData { get; private set; }

        public AudioEventArgs(AudioData audioData)
        {
            AudioData = audioData;
        }
    }
}