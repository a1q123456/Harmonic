using System;

namespace Harmonic.Networking.Flv.Data;

public class AudioData
{
    public AacPacketType? AacPacketType { get; set; } = null;
    public ReadOnlyMemory<byte> Data { get; set; }
}