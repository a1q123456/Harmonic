
using System.Threading.Tasks;

namespace RtmpSharp.IO.AMF0.AMFWriters
{
    class Amf0CharWriter : IAmfItemWriter
    {
        public void WriteData(AmfWriter writer, object obj)
        {
            writer.WriteMarker(Amf0TypeMarkers.String);
            writer.WriteUtfPrefixed(obj.ToString());
        }

        public void WriteDataAsync(AmfWriter writer, object obj)
        {
            writer.WriteMarkerAsync(Amf0TypeMarkers.String);
            writer.WriteUtfPrefixedAsync(obj.ToString());
        }
    }
}
