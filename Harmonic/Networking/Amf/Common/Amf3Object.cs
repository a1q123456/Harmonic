using Harmonic.Networking.Amf.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Harmonic.Networking.Amf.Common;

public class AmfObject : IDynamicObject, IEnumerable
{
    private Dictionary<string, object> _fields = new();

    private Dictionary<string, object> _dynamicFields = new();

    public bool IsAnonymous { get => GetType() == typeof(AmfObject); }
    public bool IsDynamic { get => _dynamicFields.Any(); }

    public IReadOnlyDictionary<string, object> DynamicFields { get => _dynamicFields; }

    public IReadOnlyDictionary<string, object> Fields { get => _fields; }

    public AmfObject()
    {

    }

    public AmfObject(Dictionary<string, object> values)
    {
        _fields = values;
    }

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
        return ((IEnumerable)Fields).GetEnumerator();
    }
}