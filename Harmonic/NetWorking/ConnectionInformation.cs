

namespace Harmonic.NetWorking
{
    public class ConnectionInformation
    {
        public string App { get; set; }
        public string Flashver { get; set; }
        public string SwfUrl { get; set; }
        public string TcUrl { get; set; }
        public bool Fpad { get; set; }
        public int AudioCodecs { get; set; }
        public int VideoCodecs { get; set; }
        int VideoFunction { get; set; }
        public string PageUrl { get; set; }
        public ObjectEncoding ObjectEncoding { get; set; }
    }
}