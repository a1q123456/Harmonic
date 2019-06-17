using RtmpSharp.Net;

namespace RtmpSharp.Messaging
{
    public abstract class RtmpEvent
    {
        public uint Timestamp { get; set; } = 0;
        public MessageType MessageType { get; set; }
        public int MessageStreamId { get; set; }

        protected RtmpEvent(MessageType messageType)
        {
            MessageType = messageType;
        }
    }
}
