using Harmonic.Networking.Rtmp.Serialization;

namespace Harmonic.Networking.Rtmp.Messages.Commands;

[RtmpCommand(Name = "receiveVideo")]
public class ReceiveVideoCommandMessage : CommandMessage
{
    [OptionalArgument]
    public bool IsReceive { get; set; }

    public ReceiveVideoCommandMessage(AmfEncodingVersion encoding) : base(encoding)
    {
    }
}