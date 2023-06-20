using Harmonic.Buffers;
using Harmonic.Networking.Amf.Serialization.Amf0;
using Harmonic.Networking.Amf.Serialization.Amf3;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Utils;
using System;

namespace Harmonic.Networking.Flv;

public class FlvMuxer
{
    private readonly Amf0Writer _amf0Writer = new();
    private readonly Amf3Writer _amf3Writer = new();

    public byte[] MultiplexFlvHeader(bool hasAudio, bool hasVideo)
    {
        var header = new byte[13];
        header[0] = 0x46;
        header[1] = 0x4C;
        header[2] = 0x56;
        header[3] = 0x01;

        byte audioFlag = 0x01 << 2;
        byte videoFlag = 0x01;
        byte typeFlag = 0x00;
        if (hasAudio) typeFlag |= audioFlag;
        if (hasVideo) typeFlag |= videoFlag;
        header[4] = typeFlag;

        NetworkBitConverter.TryGetBytes(9, header.AsSpan(5));
        return header;
    }

    public byte[] MultiplexFlv(Message data)
    {
        var dataBuffer = new ByteBuffer();
        var buffer = new byte[4];

        if (data.MessageHeader.MessageLength == 0)
        {
            var messageBuffer = new ByteBuffer();
            var context = new Networking.Rtmp.Serialization.SerializationContext()
            {
                Amf0Writer = _amf0Writer,
                Amf3Writer = _amf3Writer,
                WriteBuffer = messageBuffer
            };

            data.Serialize(context);
            var length = messageBuffer.Length;
            data.MessageHeader.MessageLength = (uint)length;
            var bodyBuffer = new byte[length];
            messageBuffer.TakeOutMemory(bodyBuffer);

            dataBuffer.WriteToBuffer((byte)data.MessageHeader.MessageType);
            NetworkBitConverter.TryGetUInt24Bytes(data.MessageHeader.MessageLength, buffer);
            dataBuffer.WriteToBuffer(buffer.AsSpan(0, 3));
            NetworkBitConverter.TryGetBytes(data.MessageHeader.Timestamp, buffer);
            dataBuffer.WriteToBuffer(buffer.AsSpan(1, 3));
            dataBuffer.WriteToBuffer(buffer.AsSpan(0, 1));
            buffer.AsSpan().Clear();
            dataBuffer.WriteToBuffer(buffer.AsSpan(0, 3));
            dataBuffer.WriteToBuffer(bodyBuffer);
               
        }
        else
        {
            dataBuffer.WriteToBuffer((byte)data.MessageHeader.MessageType);
            NetworkBitConverter.TryGetUInt24Bytes(data.MessageHeader.MessageLength, buffer);
            dataBuffer.WriteToBuffer(buffer.AsSpan(0, 3));
            NetworkBitConverter.TryGetBytes(data.MessageHeader.Timestamp, buffer);
            dataBuffer.WriteToBuffer(buffer.AsSpan(1, 3));
            dataBuffer.WriteToBuffer(buffer.AsSpan(0, 1));
            buffer.AsSpan().Clear();
            dataBuffer.WriteToBuffer(buffer.AsSpan(0, 3));
            var context = new Networking.Rtmp.Serialization.SerializationContext()
            {
                Amf0Writer = _amf0Writer,
                Amf3Writer = _amf3Writer,
                WriteBuffer = dataBuffer
            };

            data.Serialize(context);
        }

        NetworkBitConverter.TryGetBytes((data.MessageHeader.MessageLength + 11), buffer);
        dataBuffer.WriteToBuffer(buffer);

        var rawData = new byte[dataBuffer.Length];
        dataBuffer.TakeOutMemory(rawData);
        return rawData;
    }
}