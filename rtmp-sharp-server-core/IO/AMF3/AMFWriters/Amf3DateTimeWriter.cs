using System;
using System.Threading.Tasks;

namespace RtmpSharp.IO.AMF3.AMFWriters
{
    class Amf3DateTimeWriter : IAmfItemWriter
    {
        public void WriteData(AmfWriter writer, object obj)
        {
            writer.WriteMarker(Amf3TypeMarkers.Date);
            writer.WriteAmf3DateTime((DateTime)obj);
        }

        public void WriteDataAsync(AmfWriter writer, object obj)
        {
            writer.WriteMarkerAsync(Amf3TypeMarkers.Date);
            writer.WriteAmf3DateTimeAsync((DateTime)obj);
        }
    }
}