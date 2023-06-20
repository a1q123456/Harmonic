using System;

namespace Harmonic.Networking.Amf.Serialization.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class ClassFieldAttribute : Attribute
{
    public string Name { get; set; } = null;
}