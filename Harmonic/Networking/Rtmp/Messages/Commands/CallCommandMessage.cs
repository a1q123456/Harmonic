namespace Harmonic.Networking.Rtmp.Messages.Commands;

public abstract class CallCommandMessage : CommandMessage
{
    public CallCommandMessage(AmfEncodingVersion encoding) : base(encoding)
    {
    }
}