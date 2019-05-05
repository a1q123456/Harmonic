using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RtmpSharp.Messaging
{
    public class ChannelDataReceivedEventArgs : EventArgs
    {
        public ChannelType type;
        public RtmpEvent e;
        public ChannelDataReceivedEventArgs(ChannelType t, RtmpEvent e)
        {
            this.type = t;
            this.e = e;
        }
    }
}
