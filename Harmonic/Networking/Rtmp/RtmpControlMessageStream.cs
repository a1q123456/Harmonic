namespace Harmonic.Networking.Rtmp;

public class RtmpControlMessageStream : RtmpMessageStream
{
    private static readonly uint CONTROL_MSID = 0;

    internal RtmpControlMessageStream(RtmpSession rtmpSession) : base(rtmpSession, CONTROL_MSID)
    {
    }
}