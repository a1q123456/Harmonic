using Complete;
using RtmpSharp.IO;
using RtmpSharp.Messaging;
using RtmpSharp.Messaging.Events;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace RtmpSharp.Net
{
    // not implemented:
    //  - shared objects
    //  - amf3 data messages
    //  - multimedia packets
    public class RtmpPacketReader : IDisposable
    {
        public bool Continue { get; set; }

        public event EventHandler<EventReceivedEventArgs> EventReceived;
        public event EventHandler<ExceptionalEventArgs> Disconnected;
        public event EventHandler<int> Aborted;

        internal readonly AmfReader reader;

        readonly Dictionary<int, ChunkHeader> rtmpHeaders;
        readonly Dictionary<int, RtmpMessage> incompleteRtmpMessages;

        // defined by the spec
        const int DefaultChunkSize = 128;
        int readChunkSize = DefaultChunkSize;

        public RtmpPacketReader(AmfReader reader)
        {
            this.reader = reader;
            this.rtmpHeaders = new Dictionary<int, ChunkHeader>();
            this.incompleteRtmpMessages = new Dictionary<int, RtmpMessage>();

            Continue = true;
        }

        void OnEventReceived(EventReceivedEventArgs e) => EventReceived?.Invoke(this, e);
        void OnDisconnected(ExceptionalEventArgs e) => Disconnected?.Invoke(this, e);

        int lastChunkStreamId = -1;
        public async Task ReadOnceAsync(CancellationToken ct = default)
        {
            var header = await ReadHeaderAsync(ct);
            if (header.ChunkStreamId != lastChunkStreamId && incompleteRtmpMessages.ContainsKey(lastChunkStreamId))
            {
                Aborted?.Invoke(this, incompleteRtmpMessages[lastChunkStreamId].MessageStreamId);
                incompleteRtmpMessages.Remove(lastChunkStreamId);
                lastChunkStreamId = -1;
                Console.WriteLine("incomplete packet");
            }
            rtmpHeaders[header.ChunkStreamId] = header;
            lastChunkStreamId = header.ChunkStreamId;
            RtmpMessage message;
            if (!incompleteRtmpMessages.TryGetValue(header.ChunkStreamId, out message) || message == null)
            {
                message = new RtmpMessage(header.MessageType, header.MessageStreamId, header.AbsoluteTimestamp, header.PacketLength);
                incompleteRtmpMessages[header.ChunkStreamId] = message;
            }

            var remainingMessageLength = message.Length - message.CurrentLength;
            var bytesToRead = Math.Min(remainingMessageLength, readChunkSize);
            var bytes = await reader.ReadBytesAsync((int)bytesToRead);
            message.AddBytes(bytes);

            if (message.IsComplete)
            {
                lastChunkStreamId = -1;
                incompleteRtmpMessages.Remove(header.ChunkStreamId);

                var @event = ParseMessage(message);
                OnEventReceived(new EventReceivedEventArgs(@event));

                // process some kinds of packets
                var chunkSizeMessage = @event as ChunkSize;
                if (chunkSizeMessage != null)
                {
                    readChunkSize = chunkSizeMessage.Size;
                }

                var abortMessage = @event as Abort;
                if (abortMessage != null)
                    incompleteRtmpMessages.Remove(abortMessage.StreamId);
            }
        }


        public void ReadOnce()
        {
            var header = ReadHeader();
            if (header == null)
            {
                return;
            }
            rtmpHeaders[header.ChunkStreamId] = header;

            RtmpMessage message;
            if (!incompleteRtmpMessages.TryGetValue(header.ChunkStreamId, out message) || message == null)
            {
                message = new RtmpMessage(header.MessageType, header.MessageStreamId, header.AbsoluteTimestamp, header.PacketLength);
                incompleteRtmpMessages[header.ChunkStreamId] = message;
            }

            var remainingMessageLength = message.Length + (header.Timestamp >= 0xFFFFFF ? 4 : 0) - message.CurrentLength;
            var bytesToRead = Math.Min(remainingMessageLength, readChunkSize);
            var bytes = reader.ReadBytes((int)bytesToRead);
            message.AddBytes(bytes);

            if (message.IsComplete)
            {
                incompleteRtmpMessages.Remove(header.ChunkStreamId);
                RtmpEvent @event = null;
                try
                {
                    @event = ParseMessage(message);
                    OnEventReceived(new EventReceivedEventArgs(@event));
                }
                catch (ProtocolViolationException)
                {
                    Debug.WriteLine($"unhandled packet type: {message.MessageType}");
                    return;
                }

                // process some kinds of packets
                if (@event is ChunkSize chunkSizeMessage)
                {
                    readChunkSize = chunkSizeMessage.Size;
                }
                else if (@event is Abort abortMessage)
                {
                    incompleteRtmpMessages.Remove(abortMessage.StreamId);
                }
            }
            return;
        }

        public void ReadLoop()
        {
            try
            {
                while (Continue)
                {
                    ReadOnce();
                }
            }
            catch (Exception ex) when (ex is EndOfStreamException || ex is IOException)
            {
                OnDisconnected(new ExceptionalEventArgs("rtmp-packet-reader", ex));
                return;
            }
            catch (Exception)
            {
#if DEBUG && WITH_KONSEKI
                Kon.DebugException($"exception occurred", ex);
#endif

                throw;
            }
        }

        static int GetChunkStreamId(byte chunkBasicHeaderByte, AmfReader reader)
        {
            var chunkStreamId = chunkBasicHeaderByte & 0x3F;
            switch (chunkStreamId)
            {
                // 2 bytes
                case 0:
                    return reader.ReadByte() + 64;

                // 3 bytes
                case 1:
                    return reader.ReadByte() + reader.ReadByte() * 256 + 64;

                // 1 byte
                default:
                    return chunkStreamId;
            }
        }

        static async Task<int> GetChunkStreamIdAsync(byte chunkBasicHeaderByte, AmfReader reader)
        {
            var chunkStreamId = chunkBasicHeaderByte & 0x3F;
            switch (chunkStreamId)
            {
                // 2 bytes
                case 0:
                    return await reader.ReadByteAsync() + 64;

                // 3 bytes
                case 1:
                    return await reader.ReadByteAsync() + await reader.ReadByteAsync() * 256 + 64;

                // 1 byte
                default:
                    return chunkStreamId;
            }
        }

        async Task<ChunkHeader> ReadHeaderAsync(CancellationToken ct = default)
        {
            var chunkBasicHeaderByte = await reader.ReadByteAsync(ct);
            var chunkStreamId = await GetChunkStreamIdAsync(chunkBasicHeaderByte, reader);
            var chunkMessageHeaderType = (ChunkMessageHeaderType)(chunkBasicHeaderByte >> 6);
            var isTimerRelative = chunkMessageHeaderType != ChunkMessageHeaderType.Complete;

            var header = new ChunkHeader()
            {
                ChunkStreamId = chunkStreamId
            };

            ChunkHeader previousHeader;
            // don't need to clone if new header, as it contains all info
            if (!rtmpHeaders.TryGetValue(chunkStreamId, out previousHeader) && chunkMessageHeaderType != ChunkMessageHeaderType.Complete)
            {
                previousHeader = header.Clone();
            }

            switch (chunkMessageHeaderType)
            {
                // 11 bytes
                case ChunkMessageHeaderType.Complete:
                    header.Timestamp = await reader.ReadUInt24Async();
                    header.AbsoluteTimestamp = header.Timestamp;
                    header.PacketLength = await reader.ReadUInt24Async();
                    header.MessageType = (MessageType)await reader.ReadByteAsync();
                    header.MessageStreamId = await reader.ReadReverseIntAsync();
                    header.ChunkMessageHeaderType = ChunkMessageHeaderType.Complete;
                    break;

                // 7 bytes
                case ChunkMessageHeaderType.SameMessageStreamId:
                    header.Timestamp = await reader.ReadUInt24Async();
                    header.PacketLength = await reader.ReadUInt24Async();
                    header.MessageType = (MessageType)await reader.ReadByteAsync();
                    header.MessageStreamId = previousHeader.MessageStreamId;
                    header.ChunkMessageHeaderType = ChunkMessageHeaderType.SameMessageStreamId;
                    break;

                // 3 bytes
                case ChunkMessageHeaderType.OnlyTimestampNotSame:
                    header.Timestamp = await reader.ReadUInt24Async();
                    header.PacketLength = previousHeader.PacketLength;
                    header.MessageType = previousHeader.MessageType;
                    header.MessageStreamId = previousHeader.MessageStreamId;
                    header.ChunkMessageHeaderType = ChunkMessageHeaderType.OnlyTimestampNotSame;
                    break;

                // 0 bytes
                case ChunkMessageHeaderType.AllSame:
                    header.AbsoluteTimestamp = previousHeader.AbsoluteTimestamp;
                    header.Timestamp = previousHeader.Timestamp;
                    header.PacketLength = previousHeader.PacketLength;
                    header.MessageType = previousHeader.MessageType;
                    header.MessageStreamId = previousHeader.MessageStreamId;
                    header.ChunkMessageHeaderType = ChunkMessageHeaderType.AllSame;
                    break;
                default:
                    throw new SerializationException("Unexpected header type: " + (int)chunkMessageHeaderType);
            }

            // extended timestamp
            if (header.Timestamp == 0xFFFFFF)
            {
                header.Timestamp = await reader.ReadUInt32Async();
            }
            if (header.ChunkMessageHeaderType == ChunkMessageHeaderType.OnlyTimestampNotSame ||
                header.ChunkMessageHeaderType == ChunkMessageHeaderType.SameMessageStreamId)
            {
                header.AbsoluteTimestamp = header.Timestamp + previousHeader.AbsoluteTimestamp;
            }
            return header;
        }

        ChunkHeader ReadHeader()
        {
            if (reader.underlying.BaseStream is NetworkStream)
            {
                var orig_stream = (NetworkStream)reader.underlying.BaseStream;

                if (!orig_stream.DataAvailable)
                {
                    return null;
                }
            }
            if (reader.underlying.BaseStream is SslStream)
            {
                var orig_stream = (SslStream)reader.underlying.BaseStream;
                throw new NotImplementedException();
            }

            // first byte of the chunk basic header
            var chunkBasicHeaderByte = reader.ReadByte();
            var chunkStreamId = GetChunkStreamId(chunkBasicHeaderByte, reader);
            var chunkMessageHeaderType = (ChunkMessageHeaderType)(chunkBasicHeaderByte >> 6);
            var isTimerRelative = chunkMessageHeaderType != ChunkMessageHeaderType.Complete;
            var header = new ChunkHeader()
            {
                ChunkStreamId = chunkStreamId,
            };

            ChunkHeader previousHeader;
            // don't need to clone if new header, as it contains all info
            if (!rtmpHeaders.TryGetValue(chunkStreamId, out previousHeader) && chunkMessageHeaderType != ChunkMessageHeaderType.Complete)
                previousHeader = header.Clone();

            switch (chunkMessageHeaderType)
            {
                // 11 bytes
                case ChunkMessageHeaderType.Complete:
                    header.Timestamp = reader.ReadUInt24();
                    header.PacketLength = reader.ReadUInt24();
                    header.MessageType = (MessageType)reader.ReadByte();
                    header.MessageStreamId = reader.ReadReverseInt();
                    break;

                // 7 bytes
                case ChunkMessageHeaderType.SameMessageStreamId:
                    header.Timestamp = reader.ReadUInt24();
                    header.PacketLength = reader.ReadUInt24();
                    header.MessageType = (MessageType)reader.ReadByte();
                    header.MessageStreamId = previousHeader.MessageStreamId;
                    break;

                // 3 bytes
                case ChunkMessageHeaderType.OnlyTimestampNotSame:
                    header.Timestamp = reader.ReadUInt24();
                    header.PacketLength = previousHeader.PacketLength;
                    header.MessageType = previousHeader.MessageType;
                    header.MessageStreamId = previousHeader.MessageStreamId;
                    break;

                // 0 bytes
                case ChunkMessageHeaderType.AllSame:
                    header.Timestamp = previousHeader.Timestamp;
                    header.PacketLength = previousHeader.PacketLength;
                    header.MessageType = previousHeader.MessageType;
                    header.MessageStreamId = previousHeader.MessageStreamId;
                    break;
                default:
                    throw new SerializationException("Unexpected header type: " + (int)chunkMessageHeaderType);
            }

            // extended timestamp
            if (header.Timestamp == 0xFFFFFF)
            {
                header.Timestamp = reader.ReadUInt32();
            }

            return header;
        }

        RtmpEvent ParsePacket(RtmpMessage message, Func<AmfReader, RtmpEvent> handler)
        {
            var memoryStream = new MemoryStream(message.Buffer, false);
            var packetReader = new AmfReader(memoryStream, reader.SerializationContext);

            var @event = handler(packetReader);
            @event.MessageStreamId = message.MessageStreamId;
            @event.MessageType = message.MessageType;
            @event.Timestamp = message.Timestamp;
            return @event;
        }
        RtmpEvent ParseMessage(RtmpMessage message)
        {
            switch (message.MessageType)
            {
                case MessageType.SetChunkSize:
                    return ParsePacket(message, r => new ChunkSize(r.ReadInt32()));
                case MessageType.AbortMessage:
                    return ParsePacket(message, r => new Abort(r.ReadInt32()));
                case MessageType.Acknowledgement:
                    return ParsePacket(message, r => new Acknowledgement(r.ReadInt32()));
                case MessageType.UserControlMessage:
                    return ParsePacket(message, r =>
                    {
                        var eventType = r.ReadUInt16();
                        var values = new List<int>();
                        while (r.Length - r.Position >= 4)
                            values.Add(r.ReadInt32());
                        return new UserControlMessage((UserControlMessageType)eventType, values.ToArray());
                    });
                case MessageType.WindowAcknowledgementSize:
                    return ParsePacket(message, r => new WindowAcknowledgementSize(r.ReadInt32()));
                case MessageType.SetPeerBandwith:
                    return ParsePacket(message, r => new PeerBandwidth(r.ReadInt32(), r.ReadByte()));
                case MessageType.Audio:
                    return ParsePacket(message, r => new AudioData(message.Buffer));
                case MessageType.Video:
                    return ParsePacket(message, r => new VideoData(message.Buffer));
                case MessageType.DataAmf0:
                    return ParsePacket(message, r => ReadCommandOrData(r, new NotifyAmf0(), message));
                case MessageType.SharedObjectAmf0:
                    break;
                case MessageType.CommandAmf0:
                    return ParsePacket(message, r => ReadCommandOrData(r, new InvokeAmf0()));
                case MessageType.DataAmf3:
                    return ParsePacket(message, r => ReadCommandOrData(r, new NotifyAmf3()));
                case MessageType.SharedObjectAmf3:
                    break;
                case MessageType.CommandAmf3:
                    return ParsePacket(message, r =>
                    {
                        // encoding? always seems to be zero
                        var unk1 = r.ReadByte();
                        return ReadCommandOrData(r, new InvokeAmf3());
                    });


                // aggregated messages only seem to be used in audio and video streams, so we should be OK until we need multimedia.
                // case MessageType.Aggregate:

                default:
#if DEBUG && RTMP_SHARP_DEV
                    // find out how to handle this message type.
                    System.Diagnostics.Debugger.Break();
#endif
                    break;
            }

            throw new ProtocolViolationException();
        }

        static RtmpEvent ReadCommandOrData(AmfReader r, Command command, RtmpMessage message = null)
        {
            var methodName = (string)r.ReadAmf0Item();
            object temp = r.ReadAmf0Item();
            if (message != null && methodName == "@setDataFrame")
            {
                command.CommandObject = temp;
            }
            else
            {
                command.InvokeId = Convert.ToInt32(temp);
                command.CommandObject = r.ReadAmf0Item();
            }


            var parameters = new List<object>();
            while (r.DataAvailable)
                parameters.Add(r.ReadAmf0Item());

            command.MethodCall = new Method(methodName, parameters.ToArray());
            return command;
        }

        #region IDisposable Support
        private bool disposedValue = false; // 要检测冗余调用

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    reader.Dispose();
                }

                // TODO: 释放未托管的资源(未托管的对象)并在以下内容中替代终结器。
                // TODO: 将大型字段设置为 null。

                disposedValue = true;
            }
        }

        // TODO: 仅当以上 Dispose(bool disposing) 拥有用于释放未托管资源的代码时才替代终结器。
        // ~RtmpPacketReader()
        // {
        //   // 请勿更改此代码。将清理代码放入以上 Dispose(bool disposing) 中。
        //   Dispose(false);
        // }

        // 添加此代码以正确实现可处置模式。
        public void Dispose()
        {
            // 请勿更改此代码。将清理代码放入以上 Dispose(bool disposing) 中。
            Dispose(true);
            // TODO: 如果在以上内容中替代了终结器，则取消注释以下行。
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
