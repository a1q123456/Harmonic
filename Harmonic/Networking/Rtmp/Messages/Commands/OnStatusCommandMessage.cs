using Harmonic.Networking.Rtmp.Serialization;

namespace Harmonic.Networking.Rtmp.Messages.Commands;

[RtmpCommand(Name = "onStatus")]
public class OnStatusCommandMessage : CommandMessage
{
    [OptionalArgument]
    public object InfoObject { get; set; }

    public OnStatusCommandMessage(AmfEncodingVersion encoding) : base(encoding)
    {
    }
}