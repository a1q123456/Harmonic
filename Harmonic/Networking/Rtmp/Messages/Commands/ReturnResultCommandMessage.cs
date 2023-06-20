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
                this.ProcedureName = "_result";
            }
            else
            {
                this.ProcedureName = "_error";
            }
            _success = value;
        }
    }

    public ReturnResultCommandMessage(AmfEncodingVersion encoding) : base(encoding)
    {
        IsSuccess = true;
    }
}