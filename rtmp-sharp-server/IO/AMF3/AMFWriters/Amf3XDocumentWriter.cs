using System.Threading.Tasks;
using System.Xml.Linq;

namespace RtmpSharp.IO.AMF3.AMFWriters
{
    class Amf3XDocumentWriter : IAmfItemWriter
    {
        public void WriteData(AmfWriter writer, object obj)
        {
            writer.WriteMarker(Amf3TypeMarkers.Xml);
            writer.WriteAmf3XDocument(obj as XDocument);
        }

        public void WriteDataAsync(AmfWriter writer, object obj)
        {
            writer.WriteMarkerAsync(Amf3TypeMarkers.Xml);
            writer.WriteAmf3XDocumentAsync(obj as XDocument);
        }
    }
}