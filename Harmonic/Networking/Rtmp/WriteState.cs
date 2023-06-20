using System.Threading.Tasks;

namespace Harmonic.Networking.Rtmp;

class WriteState
{
    public byte[] _buffer;
    public int _length;
    public TaskCompletionSource<int> _taskSource = null;
}