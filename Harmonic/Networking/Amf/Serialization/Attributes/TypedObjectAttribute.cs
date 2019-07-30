using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Amf.Attributes
{
    [AttributeUsage(AttributeTargets.Class)]
    public class TypedObjectAttribute : Attribute
    {
        public string Name { get; set; } = null;
    }
}
