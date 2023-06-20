using Harmonic.Networking.Rtmp.Serialization;

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