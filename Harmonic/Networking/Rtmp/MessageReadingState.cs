namespace Harmonic.Networking.Rtmp;

class MessageReadingState
{
    public uint _messageLength;
    public byte[] _body;
    public int _currentIndex;
    public long RemainBytes
    {
        get => _messageLength - _currentIndex;
    }
    public bool IsCompleted
    {
        get => RemainBytes == 0;
    }
}