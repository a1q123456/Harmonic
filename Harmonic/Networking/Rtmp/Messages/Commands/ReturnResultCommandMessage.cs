using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reflection;
using System.Text;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Serialization;

namespace Harmonic.Networking.Rtmp.Messages.Commands
{
    public class ReturnResultCommandMessage : CallCommandMessage
    {
        [OptionalArgument]
        public object ReturnValue { get; set; }

        public ReturnResultCommandMessage(AmfEncodingVersion encoding) : base(encoding)
        {
        }
    }
}
