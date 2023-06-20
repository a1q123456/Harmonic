using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Harmonic.Networking.Amf.Serialization.Amf3;

public class Vector<T> : List<T>, IEquatable<List<T>>
{
    private List<T> _data = new();
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

    public bool Equals(List<T> other)
    {
        return other.SequenceEqual(this);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var d in _data)
        {
            hash.Add(d);
        }
        hash.Add(IsFixedSize);
        return hash.ToHashCode();
    }
}