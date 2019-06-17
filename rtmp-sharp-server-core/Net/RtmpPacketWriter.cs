using Complete;
using RtmpSharp.IO;
using RtmpSharp.Messaging;
using RtmpSharp.Messaging.Events;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RtmpSharp.Net
{
    // has noop write scheduling property until we implement fair writing
    // shared objects not implemented (yet)
    class RtmpPacketWriter
    {
        public bool Continue { get; set; }

        public event EventHandler<ExceptionalEventArgs> Disconnected;

        readonly Dictionary<int, ChunkHeader> chunkHeaders;
        readonly ObjectEncoding objectEncoding;

        // defined by the spec
        const int DefaultChunkSize = 128;
        public int writeChunkSize = DefaultChunkSize;
        private int packetAvailable = 0;
        private SerializationContext context = null;
        private ConcurrentQueue<byte[]> chunkQueue = new ConcurrentQueue<byte[]>();
        private SemaphoreSlim signal = new SemaphoreSlim(0);
        private DateTime epoch;
        private Random random = new Random();
        private readonly int PROTOCOL_MIN_CSID = 3;
        private readonly int PROTOCOL_MAX_CSID = 65599;
        internal bool SingleMessageStreamId { get; set; } = true;
        private object queueChunkLocker = new object();
        private Stream stream = null;

        public RtmpPacketWriter(Stream stream, SerializationContext context, ObjectEncoding objectEncoding)
        {
            this.objectEncoding = objectEncoding;
            this.stream = stream;
            this.context = context;
            chunkHeaders = new Dictionary<int, ChunkHeader>();
            Continue = true;
            epoch = DateTime.Now;
        }

        void OnDisconnected(ExceptionalEventArgs e)
        {
            Continue = false;

            if (Disconnected != null)
                Disconnected(this, e);
        }

        public async Task WriteOnceAsync(CancellationToken ct = default)
        {
            await signal.WaitAsync();
            if (chunkQueue.TryDequeue(out var buffer))
            {
                await stream.WriteAsync(buffer, 0, buffer.Length, ct);
                ct.ThrowIfCancellationRequested();
            }
        }

        internal void QueueChunk(byte[] buffer)
        {
            chunkQueue.Enqueue(buffer);
            signal.Release();
        }

        private HashSet<int> sentMessageStreamId = new HashSet<int>();

        private bool IsFirstChunkOfMessageStream(int messageStreamId)
        {
            return sentMessageStreamId.Add(messageStreamId);
        }
        void ChooseChunkHeaderType(ChunkHeader header, ChunkHeader previousHeader)
        {
            header.ChunkMessageHeaderType = ChunkMessageHeaderType.Complete;
            if (previousHeader != null)
            {
                if (header.AbsoluteTimestamp == previousHeader.AbsoluteTimestamp &&
                    header.MessageType == previousHeader.MessageType &&
                    header.PacketLength == previousHeader.PacketLength &&
                    header.MessageStreamId == previousHeader.MessageStreamId)
                {
                    header.ChunkMessageHeaderType = ChunkMessageHeaderType.AllSame;
                }
                else if (header.MessageType == previousHeader.MessageType &&
                        header.PacketLength == previousHeader.PacketLength &&
                        header.MessageStreamId == previousHeader.MessageStreamId)
                {
                    header.ChunkMessageHeaderType = ChunkMessageHeaderType.OnlyTimestampNotSame;
                }
                else if (header.MessageStreamId == previousHeader.MessageStreamId)
                {
                    header.ChunkMessageHeaderType = ChunkMessageHeaderType.SameMessageStreamId;
                }
            }
        }

        private HashSet<int> sendingChunk = new HashSet<int>();

        private int NewChunkStreamId()
        {
            var success = false;
            int ret = 0;
            while (!success)
            {
                ret = random.Next(PROTOCOL_MIN_CSID, PROTOCOL_MAX_CSID);
                success = sendingChunk.Add(ret);
            }
            return ret;
        }

        static int GetBasicHeaderLength(int streamId)
        {
            if (streamId >= 320)
                return 3;
            if (streamId >= 64)
                return 2;
            return 1;
        }

        public void WriteMessage(RtmpEvent @event, int messageStreamId, int chunkStreamId)
        {
            var buffer = GetRtmpEventBytes(@event);
            var message = new RtmpMessage(@event.MessageType, messageStreamId, @event.Timestamp, (uint)buffer.Length);
            message.AddBytes(buffer);

            for (var i = 0; i < buffer.Length; i += writeChunkSize)
            {
                lock (queueChunkLocker)
                {
                    byte[] headerBuffer = null;
                    using (var writer = new AmfWriter(context, objectEncoding))
                    {
                        var header = new ChunkHeader();
                        header.PacketLength = message.Length;
                        header.MessageType = message.MessageType;
                        header.MessageStreamId = messageStreamId;
                        header.ChunkStreamId = chunkStreamId;
                        header.AbsoluteTimestamp = message.Timestamp;
                        chunkHeaders.TryGetValue(chunkStreamId, out var previousChunk);
                        chunkHeaders[chunkStreamId] = header;

                        ChooseChunkHeaderType(header, previousChunk);
                        if (header.ChunkMessageHeaderType == ChunkMessageHeaderType.AllSame)
                        {
                            header.Timestamp = previousChunk.Timestamp;
                        }
                        else if (header.IsRelativeTimestamp)
                        {
                            header.Timestamp = message.Timestamp - previousChunk.AbsoluteTimestamp;
                        }

                        WriteChunkHeader(writer, header);
                        headerBuffer = writer.GetBytes();
                    }

                    var bytesToWrite = i + writeChunkSize > message.Length ? message.Length - i : writeChunkSize;
                    var chunkBuffer = new byte[headerBuffer.Length + bytesToWrite];
                    Buffer.BlockCopy(headerBuffer, 0, chunkBuffer, 0, headerBuffer.Length);
                    Buffer.BlockCopy(buffer, i, chunkBuffer, headerBuffer.Length, (int)bytesToWrite);

                    QueueChunk(chunkBuffer);
                }
            }

            if (@event is ChunkSize chunkSizeMsg)
            {
                writeChunkSize = chunkSizeMsg.Size;
            }
        }

        void WriteChunkBasicHeader(AmfWriter writer, ChunkMessageHeaderType messageHeaderFormat, int streamId)
        {
            var fmt = (byte)messageHeaderFormat;
            if (streamId <= 63)
            {
                writer.WriteByte((byte)((fmt << 6) + streamId));
            }
            else if (streamId <= 320)
            {
                writer.WriteByte((byte)(fmt << 6));
                writer.WriteByte((byte)(streamId - 64));
            }
            else
            {
                writer.WriteByte((byte)((fmt << 6) | 1));
                writer.WriteByte((byte)((streamId - 64) & 0xff));
                writer.WriteByte((byte)((streamId - 64) >> 8));
            }
        }

        void WriteChunkHeader(AmfWriter writer, ChunkHeader header)
        {
            WriteChunkBasicHeader(writer, header.ChunkMessageHeaderType, header.ChunkStreamId);
            // write chunk message header
            uint uint24Timestamp = header.Timestamp < 0xFFFFFF ? header.Timestamp : 0xFFFFFF;
            switch (header.ChunkMessageHeaderType)
            {
                case ChunkMessageHeaderType.Complete:
                    writer.WriteUInt24(uint24Timestamp);
                    writer.WriteUInt24(header.PacketLength);
                    writer.WriteByte((byte)header.MessageType);
                    writer.WriteReverseInt(header.MessageStreamId);
                    break;
                case ChunkMessageHeaderType.SameMessageStreamId:
                    writer.WriteUInt24(uint24Timestamp);
                    writer.WriteUInt24(header.PacketLength);
                    writer.WriteByte((byte)header.MessageType);
                    break;
                case ChunkMessageHeaderType.OnlyTimestampNotSame:
                    writer.WriteUInt24(uint24Timestamp);
                    break;
                case ChunkMessageHeaderType.AllSame:
                    break;
                default:
                    throw new ArgumentException("headerType");
            }

            // write timestamp
            if (uint24Timestamp >= 0xFFFFFF)
            {
                writer.WriteUInt32(header.Timestamp);
            }
        }

        byte[] GetRtmpEventBytes(RtmpEvent message, Action<AmfWriter, RtmpEvent> handler)
        {
            using (var messageWriter = new AmfWriter(context, objectEncoding))
            {
                handler(messageWriter, message);
                return messageWriter.GetBytes();
            }
        }
        byte[] GetRtmpEventBytes(RtmpEvent message)
        {
            switch (message.MessageType)
            {
                case MessageType.SetChunkSize:
                    return GetRtmpEventBytes(message, (w, o) => w.WriteInt32(((ChunkSize)o).Size));
                case MessageType.AbortMessage:
                    return GetRtmpEventBytes(message, (w, o) => w.WriteInt32(((Abort)o).StreamId));
                case MessageType.Acknowledgement:
                    return GetRtmpEventBytes(message, (w, o) => w.WriteInt32(((Acknowledgement)o).BytesRead));
                case MessageType.UserControlMessage:
                    return GetRtmpEventBytes(message, (w, o) =>
                    {
                        var m = (UserControlMessage)o;
                        w.WriteUInt16((ushort)m.EventType);
                        foreach (var v in m.Values)
                            w.WriteInt32(v);
                    });
                case MessageType.WindowAcknowledgementSize:
                    return GetRtmpEventBytes(message, (w, o) => w.WriteInt32(((WindowAcknowledgementSize)o).Count));
                case MessageType.SetPeerBandwith:
                    return GetRtmpEventBytes(message, (w, o) =>
                    {
                        var m = (PeerBandwidth)o;
                        w.WriteInt32(m.AcknowledgementWindowSize);
                        w.WriteByte((byte)m.LimitType);
                    });
                case MessageType.Audio:
                case MessageType.Video:
                    return GetRtmpEventBytes(message, (w, o) => WriteData(w, o, ObjectEncoding.Amf0));
                case MessageType.DataAmf0:
                    return GetRtmpEventBytes(message, (w, o) => WriteCommandOrData(w, o, ObjectEncoding.Amf0));
                case MessageType.SharedObjectAmf0:
                    return new byte[0]; // todo: `SharedObject`s
                case MessageType.CommandAmf0:
                    return GetRtmpEventBytes(message, (w, o) => WriteCommandOrData(w, o, ObjectEncoding.Amf0));
                case MessageType.DataAmf3:
                    return GetRtmpEventBytes(message, (w, o) => WriteData(w, o, ObjectEncoding.Amf3));
                case MessageType.SharedObjectAmf3:
                    return new byte[0]; // todo: `SharedObject`s
                case MessageType.CommandAmf3:
                    return GetRtmpEventBytes(message, (w, o) =>
                    {
                        w.WriteByte(0);
                        WriteCommandOrData(w, o, ObjectEncoding.Amf3);
                    });

                case MessageType.Aggregate:
                    // todo: Aggregate messages
                    System.Diagnostics.Debugger.Break();
                    return new byte[0]; // todo: `Aggregate`
                default:
                    throw new ArgumentOutOfRangeException("Unknown RTMP message type: " + (int)message.MessageType);
            }
        }

        void WriteData(AmfWriter writer, RtmpEvent o, ObjectEncoding encoding)
        {
            if (o is Command)
                WriteCommandOrData(writer, o, encoding);
            else if (o is ByteData)
                writer.WriteBytes(((ByteData)o).Data);
        }

        void WriteCommandOrData(AmfWriter writer, RtmpEvent o, ObjectEncoding encoding)
        {
            var command = o as Command;
            var methodCall = command.MethodCall;
            var isInvoke = command is Invoke;

            // write the method name or result type (first section)
            var isRequest = methodCall.CallStatus == CallStatus.Request;
            if (isRequest)
                writer.WriteAmfItem(encoding, methodCall.Name);
            else
                writer.WriteAmfItem(encoding, methodCall.IsSuccess ? "_result" : "_error");

            if (methodCall.Name == "@setDataFrame")
            {
                writer.WriteAmfItem(encoding, command.CommandObject);
            }

            if (isInvoke)
            {
                writer.WriteAmfItem(encoding, command.InvokeId);
                writer.WriteAmfItem(encoding, command.CommandObject);


                if (!methodCall.IsSuccess)
                    methodCall.Parameters = new object[] { new StatusAsObject(StatusCode.CallFailed, "error", "Call failed.") };

            }
            // write arguments
            foreach (var arg in methodCall.Parameters)
                writer.WriteAmfItem(encoding, arg);
        }
    }
}
