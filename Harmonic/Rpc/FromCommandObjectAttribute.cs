using System;

namespace Harmonic.Rpc
{
    public class FromCommandObjectAttribute : Attribute
    {
        public string Key { get; set; }
    }
}