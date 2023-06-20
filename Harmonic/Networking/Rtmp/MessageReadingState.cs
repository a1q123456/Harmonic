namespace Harmonic.Networking.Rtmp;

class MessageReadingState
{
    public uint MessageLength;
    public byte[] Body;
    public int CurrentIndex;
    public long RemainBytes
    {
        get => MessageLength - CurrentIndex;
    }
    public bool IsCompleted
    {
        get => RemainBytes == 0;
    }
}