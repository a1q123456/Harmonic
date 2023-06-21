using System;
using System.Collections.Generic;
using System.Linq;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Serialization;
using Harmonic.Networking.Utils;

namespace Harmonic.Networking.Rtmp.Messages;

internal class MessageData
{
    public MessageHeader Header { get; set; }
    public int DataOffset { get; set; }
    public uint DataLength { get; set; }
}

[RtmpMessage(MessageType.AggregateMessage)]
internal class AggregateMessage : Message
{
    public List<MessageData> Messages { get; set; } = new();
    public byte[] MessageBuffer { get; set; } = null;

    private MessageData DeserializeMessage(Span<byte> buffer, out int consumed)
    {
        consumed = 0;
        var header = new MessageHeader();
        header.MessageType = (MessageType)buffer[0];
        buffer = buffer[sizeof(byte)..];
        consumed += sizeof(byte);
        header.MessageLength = NetworkBitConverter.ToUInt24(buffer);
        buffer = buffer[3..];
        consumed += 3;
        header.Timestamp = NetworkBitConverter.ToUInt32(buffer);
        buffer = buffer[sizeof(uint)..];
        consumed += sizeof(uint);
        header.MessageStreamId = header.MessageStreamId;
        // Override message stream id
        buffer = buffer[3..];
        consumed += 3;
        var offset = consumed;
        consumed += (int)header.MessageLength;

        header.Timestamp += MessageHeader.Timestamp;

        return new MessageData
        {
            Header = header,
            DataOffset = offset,
            DataLength = header.MessageLength
        };
    }

    public override void Deserialize(SerializationContext context)
    {
        var spanBuffer = context.ReadBuffer.Span;
        while (spanBuffer.Length != 0)
        {
            Messages.Add(DeserializeMessage(spanBuffer, out var consumed));
            spanBuffer = spanBuffer[(consumed + /* back pointer */ 4)..];
        }
    }

    public override void Serialize(SerializationContext context)
    {
        int bytesNeed = (int)(Messages.Count * 11 + Messages.Sum(m => m.DataLength));
        var buffer = _arrayPool.Rent(bytesNeed);
        try
        {
            var span = buffer.AsSpan(0, bytesNeed);
            int consumed = 0;
            foreach (var message in Messages)
            {
                span[0] = (byte)message.Header.MessageType;
                span = span[sizeof(byte)..];
                NetworkBitConverter.TryGetUInt24Bytes(message.Header.MessageLength, span);
                span = span[3..];
                NetworkBitConverter.TryGetBytes(message.Header.Timestamp, span);
                span = span[4..];
                NetworkBitConverter.TryGetUInt24Bytes((uint)MessageHeader.MessageStreamId, span);
                span = span[3..];
                MessageBuffer.AsSpan(consumed, (int)message.Header.MessageLength).CopyTo(span);
                consumed += (int)message.Header.MessageLength;
                span = span[(int)message.Header.MessageLength..];
            }
            context.WriteBuffer.WriteToBuffer(span);
        }
        finally
        {
            _arrayPool.Return(buffer);
        }
    }
}