using Harmonic.Buffers;
using Harmonic.Networking;
using Harmonic.Networking.Amf.Common;
using Harmonic.Networking.Amf.Serialization.Amf0;
using Harmonic.Networking.Amf.Serialization.Amf3;
using Harmonic.Networking.Flv.Data;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Messages;
using Harmonic.Networking.Utils;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Harmonic.Hosting.RtmpServerOptions;

namespace Harmonic.Networking.Flv
{
    public class FlvDemuxer
    {
        private Amf0Reader _amf0Reader = new Amf0Reader();
        private Amf3Reader _amf3Reader = new Amf3Reader();
        private ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
        private Stream _stream = null;
        private IReadOnlyDictionary<MessageType, MessageFactory> _factories = null;

        public FlvDemuxer(IReadOnlyDictionary<MessageType, MessageFactory> factories)
        {

            _factories = factories;
        }

        public async Task<byte[]> AttachStream(Stream stream, bool disposeOld = false)
        {
            if (disposeOld)
            {
                _stream?.Dispose();
            }
            var headerBuffer = new byte[9];
            await stream.ReadBytesAsync(headerBuffer);
            _stream = stream;
            return headerBuffer;
        }

        public void SeekNoLock(double milliseconds, Dictionary<string, object> metaData, CancellationToken ct = default)
        {
            if (metaData == null)
            {
                return;
            }
            var seconds = milliseconds / 1000;
            var keyframes = metaData["keyframes"] as AmfObject;
            var times = keyframes.Fields["times"] as List<object>;
            var idx = times.FindIndex(t => ((double)t) >= seconds);
            if (idx == -1)
            {
                return;
            }
            var filePositions = keyframes.Fields["filepositions"] as List<object>;
            var pos = (double)filePositions[idx];
            _stream.Seek((int)(pos - 4), SeekOrigin.Begin);
        }

        private async Task<MessageHeader> ReadHeader(CancellationToken ct = default)
        {
            byte[] headerBuffer = null;
            byte[] timestampBuffer = null;
            try
            {
                headerBuffer = _arrayPool.Rent(15);
                timestampBuffer = _arrayPool.Rent(4);
                await _stream.ReadBytesAsync(headerBuffer.AsMemory(0, 15), ct);
                var type = (MessageType)headerBuffer[4];
                var length = NetworkBitConverter.ToUInt24(headerBuffer.AsSpan(5, 3));

                headerBuffer.AsSpan(8, 3).CopyTo(timestampBuffer.AsSpan(1));
                timestampBuffer[0] = headerBuffer[11];
                var timestamp = NetworkBitConverter.ToInt32(timestampBuffer.AsSpan(0, 4));
                var streamId = NetworkBitConverter.ToUInt24(headerBuffer.AsSpan(12, 3));
                var header = new MessageHeader()
                {
                    MessageLength = length,
                    MessageStreamId = streamId,
                    MessageType = type,
                    Timestamp = (uint)timestamp
                };
                return header;
            }
            finally
            {
                if (headerBuffer != null)
                {
                    _arrayPool.Return(headerBuffer);
                }
                if (timestampBuffer != null)
                {
                    _arrayPool.Return(timestampBuffer);
                }
            }
        }

        public FlvAudioData DemultiplexAudioData(AudioMessage message)
        {
            var head = message.Data.Span[0];
            var soundFormat = (SoundFormat)(head >> 4);
            var soundRate = (SoundRate)((head & 0x0C) >> 2);
            var soundSize = (SoundSize)(head & 0x02);
            var soundType = (SoundType)(head & 0x01);
            var ret = new FlvAudioData();
            ret.SoundFormat = soundFormat;
            ret.SoundRate = soundRate;
            ret.SoundSize = soundSize;
            ret.SoundType = soundType;
            ret.AudioData = new AudioData();

            if (soundFormat == SoundFormat.Aac)
            {
                ret.AudioData.AacPacketType = (AacPacketType)message.Data.Span[1];
                ret.AudioData.Data = message.Data.Slice(2);
            }
            ret.AudioData.Data = message.Data.Slice(1);
            return ret;
        }

        public FlvVideoData DemultiplexVideoData(VideoMessage message)
        {
            var ret = new FlvVideoData();
            var head = message.Data.Span[0];
            ret.FrameType = (FrameType)(head >> 4);
            ret.CodecId = (CodecId)(head & 0x0F);
            ret.VideoData = message.Data.Slice(1);
            return ret;
        }

        public async Task<Message> DemultiplexFlvAsync(CancellationToken ct = default)
        {
            byte[] bodyBuffer = null;

            try
            {
                var header = await ReadHeader(ct);

                bodyBuffer = _arrayPool.Rent((int)header.MessageLength);
                if (!_factories.TryGetValue(header.MessageType, out var factory))
                {
                    throw new InvalidOperationException();
                }

                await _stream.ReadBytesAsync(bodyBuffer.AsMemory(0, (int)header.MessageLength), ct);

                var context = new Networking.Rtmp.Serialization.SerializationContext()
                {
                    Amf0Reader = _amf0Reader,
                    Amf3Reader = _amf3Reader,
                    ReadBuffer = bodyBuffer.AsMemory(0, (int)header.MessageLength)
                };

                var message = factory(header, context, out var consumed);
                context.ReadBuffer = context.ReadBuffer.Slice(consumed);
                message.MessageHeader = header;
                message.Deserialize(context);
                _amf0Reader.ResetReference();
                _amf3Reader.ResetReference();
                return message;
            }
            finally
            {
                if (bodyBuffer != null)
                {
                    _arrayPool.Return(bodyBuffer);
                }
            }
        }
    }
}
