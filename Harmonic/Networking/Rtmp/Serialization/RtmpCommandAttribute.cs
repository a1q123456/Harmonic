using System;

namespace Harmonic.Networking.Rtmp.Serialization;

[AttributeUsage(AttributeTargets.Class)]
public class RtmpCommandAttribute : Attribute
{
    public string? Name { get; set; }
}