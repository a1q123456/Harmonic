
using System.Threading;
using System.Threading.Tasks;

namespace RtmpSharp.IO.AMF3
{
    public interface IExternalizable
    {
        void ReadExternal(IDataInput input);
        void WriteExternal(IDataOutput output);
        
        Task ReadExternalAsync(IDataInput input, CancellationToken ct = default(CancellationToken));
    }
}
