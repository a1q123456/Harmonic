using System.Threading.Tasks;

namespace Harmonic.Networking.Rtmp;

class WriteState
{
    public byte[] Buffer;
    public int Length;
    public TaskCompletionSource<int> TaskSource = null;
}