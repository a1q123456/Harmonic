using Harmonic.Buffers;
using System;

namespace Harmonic.Networking.Amf.Data;

public interface IExternalizable
{
    bool TryDecodeData(Span<byte> buffer, out int consumed);

    bool TryEncodeData(ByteBuffer buffer);
}