using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.BitConverters.Amf3
{
    public class Vector<T> : List<T>
    {
        private List<T> _data = new List<T>();
        public bool FixedSize { get; set; } = false;

        public new void Add(T item)
        {
            if (FixedSize)
            {
                throw new NotSupportedException();
            }
            ((List<T>)this).Add(item);
        }
    }
}
