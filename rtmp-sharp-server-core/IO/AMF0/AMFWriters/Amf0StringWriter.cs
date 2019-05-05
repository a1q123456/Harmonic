
using System.Threading.Tasks;

namespace RtmpSharp.IO.AMF0.AMFWriters
{
    class Amf0StringWriter : IAmfItemWriter
    {
        public void WriteData(AmfWriter writer, object obj)
        {
            writer.WriteAmf0StringSpecial(obj as string);
        }

        public void WriteDataAsync(AmfWriter writer, object obj)
        {
            writer.WriteAmf0StringSpecialAsync(obj as string);
        }
    }
}
