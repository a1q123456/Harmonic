
using System;
using System.Threading.Tasks;

namespace RtmpSharp.IO.AMF0.AMFWriters
{
    class Amf0AsObjectWriter : IAmfItemWriter
    {
        public void WriteData(AmfWriter writer, object obj)
        {
            writer.WriteAmf0AsObject(obj as AsObject);
        }

        public void WriteDataAsync(AmfWriter writer, object obj)
        {
            writer.WriteAmf0AsObjectAsync(obj as AsObject);
        }
    }
}
