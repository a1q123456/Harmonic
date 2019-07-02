using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Networking.Amf.Attributes
{
    public class TypedObjectAttribute : Attribute
    {
        public string Name { get; set; } = null;
    }
}
