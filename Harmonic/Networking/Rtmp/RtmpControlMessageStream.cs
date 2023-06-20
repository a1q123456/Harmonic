namespace Harmonic.Networking.Rtmp;

public class RtmpControlMessageStream : RtmpMessageStream
{
    private static readonly uint _controlMsid = 0;

    internal RtmpControlMessageStream(RtmpSession rtmpSession) : base(rtmpSession, _controlMsid)
    {
    }
}