using RtmpSharp.Net;

namespace RtmpSharp.Messaging.Events
{
    public abstract class ByteData : RtmpEvent
    {
        public byte[] Data { get; }

        protected ByteData(byte[] data, MessageType messageType) : base(messageType)
        {
            Data = data;
        }
    }

    public class AudioData : ByteData
    {
        public AudioData(byte[] data) : base(data, Net.MessageType.Audio)
        {
        }
    }

    public class VideoData : ByteData
    {
        public VideoData(byte[] data) : base(data, Net.MessageType.Video)
        {
        }
    }
}
