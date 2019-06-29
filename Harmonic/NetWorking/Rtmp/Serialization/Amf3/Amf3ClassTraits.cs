using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Serialization.Amf3
{
    public enum Amf3ClassType
    {
        Anonymous,
        Typed,
        Dynamic,
        Externalizable
    }

    public class Amf3ClassTraits : IEquatable<Amf3ClassTraits>
    {
        public Amf3ClassType ClassType { get; set; }
        public string ClassName { get; set; }
        public List<string> Members { get; set; } = new List<string>();

        public override bool Equals(object obj)
        {
            if (obj is Amf3ClassTraits traits)
            {
                Equals(traits);
            }

            return base.Equals(obj);
        }

        public bool Equals(Amf3ClassTraits traits)
        {
            return traits.ClassType == ClassType &&
                traits.ClassName == ClassName &&
                traits.Members.SequenceEqual(Members);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(ClassType);
            hash.Add(ClassName);
            foreach (var member in Members)
            {
                hash.Add(member);
            }
            return hash.ToHashCode();
        }
    }
}
