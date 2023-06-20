using System;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Serialization;
using Harmonic.Networking.Utils;

namespace Harmonic.Networking.Rtmp.Messages;

[RtmpMessage(MessageType.SetChunkSize)]
public class SetChunkSizeMessage : ControlMessage
{
    public uint ChunkSize { get; set; }

    public SetChunkSizeMessage() : base()
    {
            
    }

    public override void Deserialize(SerializationContext context)
    {
        var chunkSize = NetworkBitConverter.ToInt32(context.ReadBuffer.Span);
        ChunkSize = (uint)chunkSize;
    }

    public override void Serialize(SerializationContext context)
    {
        var buffer = this._arrayPool.Rent(sizeof(uint));
        try
        {
            NetworkBitConverter.TryGetBytes(ChunkSize, buffer);
            context.WriteBuffer.WriteToBuffer(buffer.AsSpan(0, sizeof(uint)));
        }
        finally
        {
            this._arrayPool.Return(buffer);
        }
            
    }
}