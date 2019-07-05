using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Text;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Serialization;

namespace Harmonic.Networking.Rtmp.Messages
{
    public class CallCommandMessage : CommandMessage
    {
        public string ProcedureName { get; set; }
        public double TranscationID { get; set; }
        public object CommandObject { get; set; }
        public object OptionalArguments { get; set; }

        public CallCommandMessage(Encoding encoding) : base(encoding)
        {
            
        }

        public override void Deserialize(SerializationContext context)
        {
            var buffer = context.ReadBuffer.AsSpan();
            if (Encoding == Encoding.Amf0)
            {
                Contract.Assert(context.Amf0Reader.TryGetString(buffer, out var name, out var consumed));
                buffer = buffer.Slice(consumed);
                Contract.Assert(context.Amf0Reader.TryGetNumber(buffer, out var txid, out consumed));
                buffer = buffer.Slice(consumed);
                context.Amf0Reader.TryGetValue(buffer, out _, out var commandObj, out consumed);
                buffer = buffer.Slice(consumed);
                context.Amf0Reader.TryGetValue(buffer, out _, out var optArg, out _);
                ProcedureName = name;
                TranscationID = txid;
                CommandObject = commandObj;
                OptionalArguments = optArg;
            }
            else
            {
                Contract.Assert(context.Amf3Reader.TryGetString(buffer, out var name, out var consumed));
                buffer = buffer.Slice(consumed);
                Contract.Assert(context.Amf3Reader.TryGetDouble(buffer, out var txid, out consumed));
                buffer = buffer.Slice(consumed);
                context.Amf3Reader.TryGetValue(buffer, out var commandObj, out consumed);
                buffer = buffer.Slice(consumed);
                context.Amf3Reader.TryGetValue(buffer, out var optArg, out _);
                ProcedureName = name;
                TranscationID = txid;
                CommandObject = commandObj;
                OptionalArguments = optArg;
            }

        }

        public override void Serialize(SerializationContext context)
        {
            if (Encoding == Encoding.Amf0)
            {
                using (var writeContext = new Amf.Serialization.Amf0.SerializationContext())
                {
                    context.Amf0Writer.WriteBytes(ProcedureName, writeContext);
                    context.Amf0Writer.WriteBytes(TranscationID, writeContext);
                    context.Amf0Writer.WriteValueBytes(CommandObject, writeContext);
                    context.Amf0Writer.WriteValueBytes(OptionalArguments, writeContext);
                    var buffer = _arrayPool.Rent(writeContext.MessageLength);
                    try
                    {
                        context.WriteBuffer.WriteToBuffer(buffer);
                    }
                    finally
                    {
                        _arrayPool.Return(buffer);
                    }
                }
            }
            else
            {
                using (var writeContext = new Amf.Serialization.Amf3.SerializationContext())
                {
                    context.Amf3Writer.WriteBytes(ProcedureName, writeContext);
                    context.Amf3Writer.WriteBytes(TranscationID, writeContext);
                    context.Amf3Writer.WriteValueBytes(CommandObject, writeContext);
                    context.Amf3Writer.WriteValueBytes(OptionalArguments, writeContext);
                    var buffer = _arrayPool.Rent(writeContext.MessageLength);
                    try
                    {
                        context.WriteBuffer.WriteToBuffer(buffer);
                    }
                    finally
                    {
                        _arrayPool.Return(buffer);
                    }
                }
            }
        }
    }
}
