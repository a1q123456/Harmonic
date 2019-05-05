using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RtmpSharp.IO.AMF3
{
    [Serializable]
    [SerializedName("flex.messaging.io.ObjectProxy")]
    class ObjectProxy : Dictionary<string, object>, IExternalizable
    {
        public void ReadExternal(IDataInput input)
        {
            var obj = input.ReadObject();
            var dictionary = obj as IDictionary<string, object>;
            if (dictionary != null)
            {
                foreach (var pair in dictionary)
                    this[pair.Key] = pair.Value;
            }
        }

        public async Task ReadExternalAsync(IDataInput input, CancellationToken ct = default)
        {
            var obj = await input.ReadObjectAsync(ct);
            var dictionary = obj as IDictionary<string, object>;
            if (dictionary != null)
            {
                foreach (var pair in dictionary)
                    this[pair.Key] = pair.Value;
            }
        }

        public void WriteExternal(IDataOutput output)
        {
            output.WriteObject(new AsObject(this));
        }
    }
}
