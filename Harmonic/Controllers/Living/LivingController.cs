using Harmonic.Rpc;

namespace Harmonic.Controllers.Living;

public class LivingController : RtmpController
{
    [RpcMethod("createStream")]
    public uint CreateStream()
    {
        var stream = RtmpSession.CreateNetStream<LivingStream>();
        return stream.MessageStream.MessageStreamId;
    }
}