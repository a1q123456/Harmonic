
using System.Threading.Tasks;

namespace RtmpSharp.IO.AMF3.AMFWriters
{
    class Amf3StringWriter : IAmfItemWriter
    {
        public void WriteData(AmfWriter writer, object obj)
        {
            writer.WriteMarker(Amf3TypeMarkers.String);
            writer.WriteAmf3Utf(obj as string);
        }

        public void WriteDataAsync(AmfWriter writer, object obj)
        {
            writer.WriteMarkerAsync(Amf3TypeMarkers.String);
            writer.WriteAmf3UtfAsync(obj as string);
        }
    }
}