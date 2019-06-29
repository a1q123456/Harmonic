using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Serialization.Amf3
{
    public class Amf3Object : IEnumerable
    {
        private Dictionary<string, object> _fields = new Dictionary<string, object>();

        private Dictionary<string, object> _dynamicFields = new Dictionary<string, object>();

        public bool IsAnonymous { get => GetType() == typeof(Amf3Object); }

        public IReadOnlyDictionary<string, object> DynamicFields { get => _dynamicFields; }

        public IReadOnlyDictionary<string, object> Fieldes { get => _fields; }

        public void Add(string memberName, object member)
        {
            _fields.Add(memberName, member);
        }

        public void AddDynamic(string memberName, object member)
        {
            _dynamicFields.Add(memberName, member);
        }

        public IEnumerator GetEnumerator()
        {
            return ((IEnumerable)Fieldes).GetEnumerator();
        }
    }
}
