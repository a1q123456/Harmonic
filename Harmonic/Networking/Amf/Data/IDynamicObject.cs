using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Networking.Amf.Data
{
    public interface IDynamicObject
    {
        IReadOnlyDictionary<string, object> DynamicFields { get; }

        void AddDynamic(string key, object data);
    }
}
