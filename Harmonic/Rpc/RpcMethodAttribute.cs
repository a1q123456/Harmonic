using System;

namespace Harmonic.Rpc
{
    public class RpcMethodAttribute : Attribute
    {
        public string Name { get; set; } = null;

        // Command will be sending on this chunk stream id
        public int ChannelId { get; set; } = 3;
    }
}