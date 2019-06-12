using System;

namespace RtmpSharp.Rpc
{
    public class FromCommandObjectAttribute : Attribute
    {
        public string Key { get; set; }
    }
}