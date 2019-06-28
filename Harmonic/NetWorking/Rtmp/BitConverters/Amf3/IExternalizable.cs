using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.BitConverters.Amf3
{
    public interface IExternalizable
    {
        bool TryDecodeData(Span<byte> buffer, out object value, out int consumed);

        bool TryEncodeData(Span<byte> buffer, object value, out int consumed);
    }
}
