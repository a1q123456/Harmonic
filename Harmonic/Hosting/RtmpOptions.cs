namespace Harmonic.Hosting
{
    public class RtmpOptions
    {
        public int Port = 1935;
        public SerializationContext SerializationContext = new SerializationContext();
        public ObjectEncoding ObjectEncoding = ObjectEncoding.Amf0;
        public string IPAddress = "0.0.0.0";
    }
}