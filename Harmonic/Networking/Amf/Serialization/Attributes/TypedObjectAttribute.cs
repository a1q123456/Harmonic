using System;

namespace Harmonic.Networking.Amf.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class TypedObjectAttribute : Attribute
{
    public string? Name { get; set; } = null;
}