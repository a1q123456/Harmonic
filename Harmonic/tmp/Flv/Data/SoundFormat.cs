using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Networking.Flv.Data
{
    public enum SoundFormat
    {
        PcmPE,
        Adpcm,
        Mp3,
        PcmLE,
        Nellymonser16k,
        Nellymonser8k,
        Nellymonser,
        G711ALawPcm,
        G711MuLawPcm,
        Aac = 10,
        Speex,
        Mp38k = 14,
        DeviceSpecificSound
    }
}
