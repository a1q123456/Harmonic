using System;
using System.Collections.Generic;
using System.Text;
using Harmonic.Networking.Rtmp.Data;

namespace Harmonic.Networking.Rtmp.Messages
{
    public enum LimitType : byte
    {
        Hard,
        Soft,
        Dynamic
    }

    public class SetPeerBandwidthMessage : ControlMessage
    {
        public uint WindowSize { get; set; }
        public LimitType LimitType { get; set; }

        public SetPeerBandwidthMessage() : base(MessageType.SetPeerBandwidth)
        {
        }
    }
}
