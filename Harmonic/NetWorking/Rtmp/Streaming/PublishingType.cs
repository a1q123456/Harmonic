using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Streaming
{
    public enum PublishingType
    {
        [PublishingTypeName("")]
        None,
        [PublishingTypeName("live")]
        Live,
        [PublishingTypeName("record")]
        Record,
        [PublishingTypeName("append")]
        Append
    }
}
