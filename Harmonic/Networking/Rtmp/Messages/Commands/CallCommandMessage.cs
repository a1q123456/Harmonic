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
    public abstract class CallCommandMessage : CommandMessage
    {
        public CallCommandMessage(AmfEncodingVersion encoding) : base(encoding)
        {
        }
    }
}
