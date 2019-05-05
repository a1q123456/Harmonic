using Complete;
using RtmpSharp.IO;
using RtmpSharp.Messaging;
using RtmpSharp.Messaging.Events;
using System;
using System.Collections.Generic;
using System.IO;
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
    public class RtmpPacketReader
    {
        public bool Continue { get; set; }

        public event EventHandler<EventReceivedEventArgs> EventReceived;
        public event EventHandler<ExceptionalEventArgs> Disconnected;

        internal readonly AmfReader reader;

        readonly Dictionary<int, RtmpHeader> rtmpHeaders;
        readonly Dictionary<int, RtmpPacket> rtmpPackets;

        // defined by the spec
        const int DefaultChunkSize = 128;
        int readChunkSize = DefaultChunkSize;

        public RtmpPacketReader(AmfReader reader)
        {
            this.reader = reader;
            this.rtmpHeaders = new Dictionary<int, RtmpHeader>();
            this.rtmpPackets = new Dictionary<int, RtmpPacket>();

            Continue = true;
        }

        void OnEventReceived(EventReceivedEventArgs e) => EventReceived?.Invoke(this, e);
        void OnDisconnected(ExceptionalEventArgs e) => Disconnected?.Invoke(this, e);

        public async Task ReadOnceAsync()
        {
            var header = await ReadHeaderAsync();
            rtmpHeaders[header.StreamId] = header;

            RtmpPacket packet;
            if (!rtmpPackets.TryGetValue(header.StreamId, out packet) || packet == null)
            {
                packet = new RtmpPacket(header);
                rtmpPackets[header.StreamId] = packet;
            }

            var remainingMessageLength = packet.Length + (header.Timestamp >= 0xFFFFFF ? 4 : 0) - packet.CurrentLength;
            var bytesToRead = Math.Min(remainingMessageLength, readChunkSize);
            var bytes = await reader.ReadBytesAsync(bytesToRead);
            packet.AddBytes(bytes);

            if (packet.IsComplete)
            {
                rtmpPackets.Remove(header.StreamId);

                var @event = ParsePacket(packet);
                OnEventReceived(new EventReceivedEventArgs(@event));

                // process some kinds of packets
                var chunkSizeMessage = @event as ChunkSize;
                if (chunkSizeMessage != null)
                {
                    readChunkSize = chunkSizeMessage.Size;
                }

                var abortMessage = @event as Abort;
                if (abortMessage != null)
                    rtmpPackets.Remove(abortMessage.StreamId);
            }
        }


        public bool ReadOnce()
        {
            var header = ReadHeader();
            if (header == null)
            {
                return false;
            }
            rtmpHeaders[header.StreamId] = header;

            RtmpPacket packet;
            if (!rtmpPackets.TryGetValue(header.StreamId, out packet) || packet == null)
            {
                packet = new RtmpPacket(header);
                rtmpPackets[header.StreamId] = packet;
            }

            var remainingMessageLength = packet.Length + (header.Timestamp >= 0xFFFFFF ? 4 : 0) - packet.CurrentLength;
            var bytesToRead = Math.Min(remainingMessageLength, readChunkSize);
            var bytes = reader.ReadBytes(bytesToRead);
            packet.AddBytes(bytes);

            if (packet.IsComplete)
            {
                rtmpPackets.Remove(header.StreamId);

                var @event = ParsePacket(packet);
                OnEventReceived(new EventReceivedEventArgs(@event));

                // process some kinds of packets
                var chunkSizeMessage = @event as ChunkSize;
                if (chunkSizeMessage != null)
                    readChunkSize = chunkSizeMessage.Size;

                var abortMessage = @event as Abort;
                if (abortMessage != null)
                    rtmpPackets.Remove(abortMessage.StreamId);
            }
            return true;
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

        async Task<RtmpHeader> ReadHeaderAsync()
        {
            var chunkBasicHeaderByte = await reader.ReadByteAsync();
            var chunkStreamId = await GetChunkStreamIdAsync(chunkBasicHeaderByte, reader);
            var chunkMessageHeaderType = (ChunkMessageHeaderType)(chunkBasicHeaderByte >> 6);

            var header = new RtmpHeader()
            {
                StreamId = chunkStreamId,
                IsTimerRelative = chunkMessageHeaderType != ChunkMessageHeaderType.New
            };

            RtmpHeader previousHeader;
            // don't need to clone if new header, as it contains all info
            if (!rtmpHeaders.TryGetValue(chunkStreamId, out previousHeader) && chunkMessageHeaderType != ChunkMessageHeaderType.New)
                previousHeader = header.Clone();

            switch (chunkMessageHeaderType)
            {
                // 11 bytes
                case ChunkMessageHeaderType.New:
                    header.Timestamp = await reader.ReadUInt24Async();
                    header.PacketLength = await reader.ReadUInt24Async();
                    header.MessageType = (MessageType) await reader.ReadByteAsync();
                    header.MessageStreamId = await reader.ReadReverseIntAsync();
                    break;

                // 7 bytes
                case ChunkMessageHeaderType.SameSource:
                    header.Timestamp = await reader.ReadUInt24Async();
                    header.PacketLength = await reader.ReadUInt24Async();
                    header.MessageType = (MessageType)await reader.ReadByteAsync();
                    header.MessageStreamId = previousHeader.MessageStreamId;
                    break;

                // 3 bytes
                case ChunkMessageHeaderType.TimestampAdjustment:
                    header.Timestamp = await reader.ReadUInt24Async();
                    header.PacketLength = previousHeader.PacketLength;
                    header.MessageType = previousHeader.MessageType;
                    header.MessageStreamId = previousHeader.MessageStreamId;
                    break;

                // 0 bytes
                case ChunkMessageHeaderType.Continuation:
                    header.Timestamp = previousHeader.Timestamp;
                    header.PacketLength = previousHeader.PacketLength;
                    header.MessageType = previousHeader.MessageType;
                    header.MessageStreamId = previousHeader.MessageStreamId;
                    header.IsTimerRelative = previousHeader.IsTimerRelative;
                    break;
                default:
                    throw new SerializationException("Unexpected header type: " + (int)chunkMessageHeaderType);
            }

            // extended timestamp
            if (header.Timestamp == 0xFFFFFF)
                header.Timestamp = await reader.ReadInt32Async();

            return header;
        }

        RtmpHeader ReadHeader()
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
            if (reader.underlying.BaseStream is WebsocketStream)
            {
                return null;
                //var orig_stream = (WebsocketStream)reader.underlying.BaseStream;

                //if (!orig_stream.DataAvailable)
                //{
                //    return null;
                //}
            }

            // first byte of the chunk basic header
            var chunkBasicHeaderByte = reader.ReadByte();
            var chunkStreamId = GetChunkStreamId(chunkBasicHeaderByte, reader);
            var chunkMessageHeaderType = (ChunkMessageHeaderType)(chunkBasicHeaderByte >> 6);

            var header = new RtmpHeader()
            {
                StreamId = chunkStreamId,
                IsTimerRelative = chunkMessageHeaderType != ChunkMessageHeaderType.New
            };

            RtmpHeader previousHeader;
            // don't need to clone if new header, as it contains all info
            if (!rtmpHeaders.TryGetValue(chunkStreamId, out previousHeader) && chunkMessageHeaderType != ChunkMessageHeaderType.New)
                previousHeader = header.Clone();

            switch (chunkMessageHeaderType)
            {
                // 11 bytes
                case ChunkMessageHeaderType.New:
                    header.Timestamp = reader.ReadUInt24();
                    header.PacketLength = reader.ReadUInt24();
                    header.MessageType = (MessageType)reader.ReadByte();
                    header.MessageStreamId = reader.ReadReverseInt();
                    break;

                // 7 bytes
                case ChunkMessageHeaderType.SameSource:
                    header.Timestamp = reader.ReadUInt24();
                    header.PacketLength = reader.ReadUInt24();
                    header.MessageType = (MessageType)reader.ReadByte();
                    header.MessageStreamId = previousHeader.MessageStreamId;
                    break;

                // 3 bytes
                case ChunkMessageHeaderType.TimestampAdjustment:
                    header.Timestamp = reader.ReadUInt24();
                    header.PacketLength = previousHeader.PacketLength;
                    header.MessageType = previousHeader.MessageType;
                    header.MessageStreamId = previousHeader.MessageStreamId;
                    break;

                // 0 bytes
                case ChunkMessageHeaderType.Continuation:
                    header.Timestamp = previousHeader.Timestamp;
                    header.PacketLength = previousHeader.PacketLength;
                    header.MessageType = previousHeader.MessageType;
                    header.MessageStreamId = previousHeader.MessageStreamId;
                    header.IsTimerRelative = previousHeader.IsTimerRelative;
                    break;
                default:
                    throw new SerializationException("Unexpected header type: " + (int)chunkMessageHeaderType);
            }

            // extended timestamp
            if (header.Timestamp == 0xFFFFFF)
                header.Timestamp = reader.ReadInt32();

            return header;
        }

        RtmpEvent ParsePacket(RtmpPacket packet, Func<AmfReader, RtmpEvent> handler)
        {
            var memoryStream = new MemoryStream(packet.Buffer, false);
            var packetReader = new AmfReader(memoryStream, reader.SerializationContext);

            var header = packet.Header;
            var message = handler(packetReader);
            message.Header = header;
            message.Timestamp = header.Timestamp;
            return message;
        }
        RtmpEvent ParsePacket(RtmpPacket packet)
        {
            switch (packet.Header.MessageType)
            {
                case MessageType.SetChunkSize:
                    return ParsePacket(packet, r => new ChunkSize(r.ReadInt32()));
                case MessageType.AbortMessage:
                    return ParsePacket(packet, r => new Abort(r.ReadInt32()));
                case MessageType.Acknowledgement:
                    return ParsePacket(packet, r => new Acknowledgement(r.ReadInt32()));
                case MessageType.UserControlMessage:
                    return ParsePacket(packet, r =>
                    {
                        var eventType = r.ReadUInt16();
                        var values = new List<int>();
                        while (r.Length - r.Position >= 4)
                            values.Add(r.ReadInt32());
                        return new UserControlMessage((UserControlMessageType)eventType, values.ToArray());
                    });
                case MessageType.WindowAcknowledgementSize:
                    return ParsePacket(packet, r => new WindowAcknowledgementSize(r.ReadInt32()));
                case MessageType.SetPeerBandwith:
                    return ParsePacket(packet, r => new PeerBandwidth(r.ReadInt32(), r.ReadByte()));
                case MessageType.Audio:
                    return ParsePacket(packet, r => new AudioData(packet.Buffer));
                case MessageType.Video:
                    return ParsePacket(packet, r => new VideoData(packet.Buffer));


                case MessageType.DataAmf0:
                    return ParsePacket(packet, r => ReadCommandOrData(r, new NotifyAmf0(), packet.Header));
                case MessageType.SharedObjectAmf0:
                    break;
                case MessageType.CommandAmf0:
                    return ParsePacket(packet, r => ReadCommandOrData(r, new InvokeAmf0()));


                case MessageType.DataAmf3:
                    return ParsePacket(packet, r => ReadCommandOrData(r, new NotifyAmf3()));
                case MessageType.SharedObjectAmf3:
                    break;
                case MessageType.CommandAmf3:
                    return ParsePacket(packet, r =>
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

            // skip messages we don't understand
            return null;
        }

        static RtmpEvent ReadCommandOrData(AmfReader r, Command command, RtmpHeader header = null)
        {
            var methodName = (string)r.ReadAmf0Item();
            object temp = r.ReadAmf0Item();
            if (header != null && methodName == "@setDataFrame")
            {
                command.ConnectionParameters = temp;
            }
            else
            {
                command.InvokeId = Convert.ToInt32(temp);
                command.ConnectionParameters = r.ReadAmf0Item();
            }
            

            var parameters = new List<object>();
            while (r.DataAvailable)
                parameters.Add(r.ReadAmf0Item());

            command.MethodCall = new Method(methodName, parameters.ToArray());
            return command;
        }
    }
}
