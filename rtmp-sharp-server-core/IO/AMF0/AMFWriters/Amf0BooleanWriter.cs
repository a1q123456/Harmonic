
using System.Threading.Tasks;

namespace RtmpSharp.IO.AMF0.AMFWriters
{
    class Amf0BooleanWriter : IAmfItemWriter
    {
        public void WriteData(AmfWriter writer, object obj)
        {
            writer.WriteMarker(Amf0TypeMarkers.Boolean);
            writer.WriteBoolean((bool)obj);
        }

        public void WriteDataAsync(AmfWriter writer, object obj)
        {
            writer.WriteMarkerAsync(Amf0TypeMarkers.Boolean);
            writer.WriteBooleanAsync((bool)obj);
        }
    }
}
