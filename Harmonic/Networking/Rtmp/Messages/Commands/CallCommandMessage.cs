using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reflection;
using System.Text;
using Harmonic.NetWorking.Rtmp.Data;
using Harmonic.NetWorking.Rtmp.Serialization;
using Harmonic.NetWorking.Rtmp.Messages;

namespace Harmonic.NetWorking.Rtmp.Messages.Commands
{
    public abstract class CallCommandMessage : CommandMessage
    {
        public CallCommandMessage(AmfEncodingVersion encoding) : base(encoding)
        {
        }
    }
}
