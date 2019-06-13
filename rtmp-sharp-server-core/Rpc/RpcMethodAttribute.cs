using System;

namespace RtmpSharp.Rpc
{
    public class RpcMethodAttribute : Attribute
    {
        public string Name { get; set; } = null;
    }
}