using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Networking.Rtmp.Data
{
    public enum MessageType : byte
    {
        #region Protocol Control Messages

        SetChunkSize = 1,
        AbortMessage = 2,
        Acknowledgement = 3,
        WindowAcknowledgementSize = 4,
        SetPeerBandwidth = 6,

        #endregion

        UserControlMessages = 4,

        #region Rtmp Command Messages
        Amf0Command = 20,
        Amf3Command = 17,
        Amf0Data = 18,
        Amf3Data = 15,
        Amf0SharedObjectMessage = 19,
        Amf3SharedObjectMessage = 16,
        AudioMessage = 8,
        VideoMessage = 9,
        AggregateMessage = 22,
        #endregion



    }
}
