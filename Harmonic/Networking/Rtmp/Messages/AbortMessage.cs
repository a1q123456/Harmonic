using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Serialization;
using Harmonic.Networking.Utils;

namespace Harmonic.Networking.Rtmp.Messages;

[RtmpMessage(MessageType.AbortMessage)]
public class AbortMessage : ControlMessage
{
    public uint AbortedChunkStreamId { get; set; }

    public AbortMessage() : base()
    {
    }

    public override void Deserialize(SerializationContext context)
    {
        AbortedChunkStreamId = NetworkBitConverter.ToUInt32(context.ReadBuffer.Span);
    }

    public override void Serialize(SerializationContext context)
    {
        var buffer = this._arrayPool.Rent(sizeof(uint));
        try
        {
            NetworkBitConverter.TryGetBytes(AbortedChunkStreamId, buffer);
            context.WriteBuffer.WriteToBuffer(buffer);
        }
        finally
        {
            this._arrayPool.Return(buffer);
        }
    }
}