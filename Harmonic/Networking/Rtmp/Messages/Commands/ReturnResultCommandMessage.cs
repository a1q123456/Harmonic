using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reflection;
using System.Text;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Serialization;
using Harmonic.Networking.Rtmp.Messages;

namespace Harmonic.Networking.Rtmp.Messages.Commands;

public class ReturnResultCommandMessage : CallCommandMessage
{
    [OptionalArgument]
    public object ReturnValue { get; set; }
    private bool _success = true;
    public bool IsSuccess
    {
        get
        {
            return _success;
        }
        set
        {
            if (value)
            {
                ProcedureName = "_result";
            }
            else
            {
                ProcedureName = "_error";
            }
            _success = value;
        }
    }

    public ReturnResultCommandMessage(AmfEncodingVersion encoding) : base(encoding)
    {
        IsSuccess = true;
    }
}