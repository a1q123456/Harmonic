using System;
using System.Threading.Tasks;

namespace RtmpSharp.IO
{
    interface IAmfItemWriter
    {
        void WriteData(AmfWriter writer, object obj);
    }
}
