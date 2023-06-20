using Harmonic.Networking.Rtmp.Serialization;

namespace Harmonic.Networking.Rtmp.Messages.Commands;

[RtmpCommand(Name = "play")]
public class PlayCommandMessage : CommandMessage
{
    [OptionalArgument]
    public string StreamName { get; set; }
    [OptionalArgument]
    public double Start { get; set; }
    [OptionalArgument]
    public double Duration { get; set; }
    [OptionalArgument]
    public bool Reset { get; set; }


    public PlayCommandMessage(AmfEncodingVersion encoding) : base(encoding)
    {
    }
}