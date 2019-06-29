using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Serialization.Amf3
{
    public interface IExternalizable
    {
        bool TryDecodeData(Span<byte> buffer, out int consumed);

        bool TryEncodeData(Span<byte> buffer, out int consumed);
    }
}
