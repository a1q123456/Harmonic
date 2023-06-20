using Harmonic.Networking.Rtmp.Serialization;

namespace Harmonic.Networking.Rtmp.Messages.Commands;

[RtmpCommand(Name = "connect")]
public class ConnectCommandMessage : CommandMessage
{
    [OptionalArgument]
    public object UserArguments { get; set; }

    public ConnectCommandMessage(AmfEncodingVersion encoding) : base(encoding)
    {
    }
}