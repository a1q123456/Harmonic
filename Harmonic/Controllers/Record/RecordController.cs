using Harmonic.Rpc;

namespace Harmonic.Controllers.Record;

public class RecordController : RtmpController
{
    [RpcMethod("createStream")]
    public uint CreateStream()
    {
        var stream = this.RtmpSession.CreateNetStream<RecordStream>();
        return stream.MessageStream.MessageStreamId;
    }
}