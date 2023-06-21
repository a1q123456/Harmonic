using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Harmonic.Networking.Amf.Serialization.Amf3;

public class Amf3Dictionary<TKey, TValue> : Dictionary<TKey, TValue>
{
    public bool WeakKeys { get; set; } = false;

    public Amf3Dictionary() { }

    public Amf3Dictionary(IDictionary<TKey, TValue> dictionary) : base(dictionary) { }

    public Amf3Dictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection) : base(collection) { }

    public Amf3Dictionary(IEqualityComparer<TKey> comparer) : base(comparer) { }

    public Amf3Dictionary(int capacity) : base(capacity) { }

    public Amf3Dictionary(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey> comparer) : base(dictionary, comparer) { }

    public Amf3Dictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey> comparer) : base(collection, comparer) { }

    public Amf3Dictionary(int capacity, IEqualityComparer<TKey> comparer) : base(capacity, comparer) { }

    protected Amf3Dictionary(SerializationInfo info, StreamingContext context) : base(info, context) { }

}