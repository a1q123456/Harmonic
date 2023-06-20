using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Harmonic.Networking.Rtmp.Streaming;

[AttributeUsage(AttributeTargets.Field)]
public class PublishingTypeNameAttribute : Attribute
{
    public string Name { get; set; }

    public PublishingTypeNameAttribute(string name)
    {
        Name = name;
    }
}

public static class PublishingHelpers
{
    public static IReadOnlyDictionary<string, PublishingType> PublishingTypes { get; }

    static PublishingHelpers()
    {
        var types = new Dictionary<string, PublishingType>();
        var enumType = typeof(PublishingType);
        var members = Enum.GetNames(enumType).Select(n => enumType.GetMember(n).First()).ToArray();

        foreach (var member in members)
        {
            var name = member.GetCustomAttribute<PublishingTypeNameAttribute>().Name;
            types.Add(name, (PublishingType)Enum.Parse(enumType, member.Name));
        }

        PublishingTypes = types;
    }

    public static bool IsTypeSupported(string type)
    {
        return PublishingTypes.ContainsKey(type);
    }
}