using Complete;
using RtmpSharp.IO;
using RtmpSharp.Messaging;
using RtmpSharp.Messaging.Events;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace RtmpSharp.Net
{
    // has noop write scheduling property until we implement fair writing
    // shared objects not implemented (yet)
    class FlvPacketWriter
    {
        public bool Continue { get; set; }

        public event EventHandler<ExceptionalEventArgs> Disconnected;

        internal readonly AmfWriter writer;
        readonly ConcurrentQueue<RtmpPacket> queuedPackets;
        readonly AutoResetEvent packetAvailableEvent;
        readonly ObjectEncoding objectEncoding;

        // defined by the spec
        const int DefaultChunkSize = 128;
        int writeChunkSize = DefaultChunkSize;

        public FlvPacketWriter(AmfWriter writer, ObjectEncoding objectEncoding)
        {
            this.objectEncoding = objectEncoding;
            this.writer = writer;
            
            queuedPackets = new ConcurrentQueue<RtmpPacket>();
            packetAvailableEvent = new AutoResetEvent(false);

            Continue = true;
        }

        void OnDisconnected(ExceptionalEventArgs e)
        {
            Continue = false;

            if (Disconnected != null)
                Disconnected(this, e);
        }

        public bool WriteOnce()
        {
            if (packetAvailableEvent.WaitOne(1))
            {
                RtmpPacket packet;
                while (queuedPackets.TryDequeue(out packet))
                    WritePacket(packet);
                return true;
            }
            return false;
        }

        public void WriteLoop()
        {
            try
            {
                while (Continue)
                {
                    WriteOnce();
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.Print("Exception: {0} at {1}", ex, ex.StackTrace);
                if (ex.InnerException != null)
                {
                    var inner = ex.InnerException;
                    System.Diagnostics.Debug.Print("InnerException: {0} at {1}", inner, inner.StackTrace);
                }
#endif

                OnDisconnected(new ExceptionalEventArgs("rtmp-packet-writer", ex));
                throw;
            }
        }

        static ChunkMessageHeaderType GetMessageHeaderType(RtmpHeader header, RtmpHeader previousHeader)
        {
            if (previousHeader == null || header.MessageStreamId != previousHeader.MessageStreamId || !header.IsTimerRelative)
                return ChunkMessageHeaderType.New;

            if (header.PacketLength != previousHeader.PacketLength || header.MessageType != previousHeader.MessageType)
                return ChunkMessageHeaderType.SameSource;

            if (header.Timestamp != previousHeader.Timestamp)  
                return ChunkMessageHeaderType.TimestampAdjustment;

            return ChunkMessageHeaderType.Continuation;
        }

        public void Queue(RtmpEvent message, int streamId, int messageStreamId)
        {
            var header = new RtmpHeader();
            var packet = new RtmpPacket(header, message);

            header.StreamId = 0;
            header.Timestamp = message.Timestamp;
            header.MessageStreamId = messageStreamId;
            header.MessageType = message.MessageType; 
            queuedPackets.Enqueue(packet);
            packetAvailableEvent.Set();
        }

        static int GetBasicHeaderLength(int streamId)
        {
            if (streamId >= 320)
                return 3;
            if (streamId >= 64)
                return 2;
            return 1;
        }

        FlvPacket RtmpPacketToFlvPacket(RtmpPacket rtmp_packet)
        {
            var rtmp_header = rtmp_packet.Header;
            var header = new FlvTagHeader();
            header.StreamId = 0;
            header.TagType = rtmp_header.MessageType;
            header.Timestamp = rtmp_header.Timestamp;
            var packet = new FlvPacket(header);
            packet.Body = rtmp_packet.Body;
            return packet;
        }

        void WritePacket(RtmpPacket packet)
        {
            var flv_packet = RtmpPacketToFlvPacket(packet);
            var header = flv_packet.Header;
            var streamId = header.StreamId;
            var message = flv_packet.Body;

            var buffer = GetMessageBytes(header, message);
            header.DataSize = buffer.Length;
            WriteTagHeader(header);
            writer.Write(buffer);
            WritePrevTagLength(buffer);
        }

        private void WritePrevTagLength(byte[] buffer)
        {
            writer.WriteUInt32((uint)buffer.Length + 11);
        }

        void WriteTagHeader(FlvTagHeader header)
        {
            writer.WriteByte((byte)header.TagType);
            writer.WriteUInt24(header.DataSize);
            var timestamp = BitConverter.GetBytes(header.Timestamp);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(timestamp);
            writer.Write(timestamp, 1, 3);
            writer.WriteByte(timestamp[0]);
            writer.WriteUInt24(header.StreamId);
            
        }

        byte[] GetMessageBytes(RtmpEvent message, Action<AmfWriter, RtmpEvent> handler)
        {
            using (var stream = new MemoryStream())
            using (var messageWriter = new AmfWriter(stream, writer.SerializationContext, objectEncoding))
            {
                handler(messageWriter, message);
                return stream.ToArray();
            }
        }

        byte[] GetMessageBytes(FlvTagHeader header, RtmpEvent message)
        {
            switch (header.TagType)
            {
                case MessageType.Audio:
                case MessageType.Video:
                    return GetMessageBytes(message, (w, o) => w.WriteBytes(((ByteData)o).Data));
                case MessageType.DataAmf0:
                    return GetMessageBytes(message, (w, o) => WriteCommandOrData(w, o, ObjectEncoding.Amf0));
                default:
                    throw new ArgumentOutOfRangeException("Unknown RTMP message type: " + (int)header.TagType);
            }
        }

        void WriteData(AmfWriter writer, RtmpEvent o, ObjectEncoding encoding)
        {
            var command = o as Command;
            if (command.MethodCall == null)
                WriteCommandOrData(writer, o, encoding);
            else
                writer.WriteBytes(command.Buffer);
        }

        void WriteCommandOrData(AmfWriter writer, RtmpEvent o, ObjectEncoding encoding)
        {
            var command = o as Command;
            var methodCall = command.MethodCall;
            var isInvoke = command is Invoke;

            // write the method name or result type (first section)
            if (methodCall.Name == "@setDataFrame")
            {
                writer.WriteAmfItem(encoding, command.ConnectionParameters);
            }
            
            // write arguments
            foreach (var arg in methodCall.Parameters)
                writer.WriteAmfItem(encoding, arg);
        }
    }
}
