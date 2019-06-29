using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Harmonic.NetWorking.Rtmp.Serialization.Amf3
{
    public class Vector<T> : List<T>
    {
        private List<T> _data = new List<T>();
        public bool IsFixedSize { get; set; } = false;

        public new void Add(T item)
        {
            if (IsFixedSize)
            {
                throw new NotSupportedException();
            }
            ((List<T>)this).Add(item);
        }

        public override bool Equals(object obj)
        {
            if (obj is Vector<T> en)
            {
                return IsFixedSize == en.IsFixedSize && en.SequenceEqual(this);
            }
            return base.Equals(obj);
        }
    }
}
