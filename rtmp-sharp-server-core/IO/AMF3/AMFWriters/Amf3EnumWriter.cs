using System;
using System.Threading.Tasks;

namespace RtmpSharp.IO.AMF3.AMFWriters
{
    class Amf3EnumWriter : IAmfItemWriter
    {
        public void WriteData(AmfWriter writer, object obj)
        {
            writer.WriteMarker(Amf3TypeMarkers.Integer);
            writer.WriteAmf3Int(Convert.ToInt32(obj));
        }

        public void WriteDataAsync(AmfWriter writer, object obj)
        {
            writer.WriteMarkerAsync(Amf3TypeMarkers.Integer);
            writer.WriteAmf3IntAsync(Convert.ToInt32(obj));
        }
    }
}