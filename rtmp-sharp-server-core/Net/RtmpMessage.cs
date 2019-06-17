using RtmpSharp.Messaging;
using System;

namespace RtmpSharp.Net
{
    class RtmpMessage
    {
        public RtmpEvent Body { get; set; }
        public byte[] Buffer { get; private set; }
        public uint Length { get; private set; }
        public int CurrentLength { get; private set; }
        public MessageType MessageType { get; set; }
        public int MessageStreamId { get; set; } = 0;
        // absolute timestamp
        public uint Timestamp { get; set; }
        public bool IsComplete => Length == CurrentLength;

        public RtmpMessage(MessageType messageType, 
                            int messageStreamId, 
                            uint timestamp, 
                            uint packetLength)
        {
            MessageType = messageType;
            MessageStreamId = messageStreamId;
            Timestamp = timestamp;
            Length = packetLength;
            Buffer = new byte[Length];
        }

        public RtmpMessage(RtmpEvent body)
        {
            Body = body;
        }

        public RtmpMessage(MessageType messageType, 
                            int messageStreamId, 
                            uint timestamp, 
                            uint packetLength, 
                            RtmpEvent body) : this(messageType, messageStreamId, timestamp, packetLength)
        {
            Body = body;
        }

        internal void InitWithBytes(byte[] data)
        {
            CurrentLength = data.Length;
            Length = (uint)data.Length;
            Buffer = data;
        }

        internal void AddBytes(byte[] bytes)
        {
            Array.Copy(bytes, 0, Buffer, CurrentLength, bytes.Length);
            CurrentLength += bytes.Length;
        }
    }
}
