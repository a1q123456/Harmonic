using RtmpSharp.Messaging;
using System;

namespace RtmpSharp.Net
{
    class FlvPacket
    {
        public FlvTagHeader Header { get; set; }
        public RtmpEvent Body { get; set; }
        public byte[] Buffer { get; private set; }
        public int Length { get; private set; }
        public int CurrentLength { get; private set; }
        public bool IsComplete => Length == CurrentLength;

        public FlvPacket(FlvTagHeader header)
        {
            Header = header;
            Length = header.DataSize;
            Buffer = new byte[Length];
        }

        public FlvPacket(RtmpEvent body)
        {
            Body = body;
        }

        public FlvPacket(FlvTagHeader header, RtmpEvent body) : this(header)
        {
            Body = body;
            Length = header.DataSize;
        }

        internal void AddBytes(byte[] bytes)
        {
            Array.Copy(bytes, 0, Buffer, CurrentLength, bytes.Length);
            CurrentLength += bytes.Length;
        }
    }
}
