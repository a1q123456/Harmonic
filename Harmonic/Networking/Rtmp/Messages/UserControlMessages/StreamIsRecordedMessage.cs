using Harmonic.NetWorking.Rtmp.Serialization;
using Harmonic.NetWorking.Utils;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Text;

namespace Harmonic.NetWorking.Rtmp.Messages.UserControlMessages
{
    [UserControlMessage(Type = UserControlEventType.StreamIsRecorded)]
    public class StreamIsRecordedMessage : UserControlMessage
    {
        public uint StreamID { get; set; }

        public StreamIsRecordedMessage() : base()
        {

        }

        public override void Deserialize(SerializationContext context)
        {
            var span = context.ReadBuffer.Span;
            var eventType = (UserControlEventType)NetworkBitConverter.ToUInt16(span);
            span = span.Slice(sizeof(ushort));
            Contract.Assert(eventType == UserControlEventType.StreamIsRecorded);
            StreamID = NetworkBitConverter.ToUInt32(span);
        }

        public override void Serialize(SerializationContext context)
        {
            var length = sizeof(ushort) + sizeof(uint);
            var buffer = _arrayPool.Rent(length);
            try
            {
                var span = buffer.AsSpan();
                NetworkBitConverter.TryGetBytes((ushort)UserControlEventType.StreamBegin, span);
                span = span.Slice(sizeof(ushort));
                NetworkBitConverter.TryGetBytes(StreamID, span);
            }
            finally
            {
                _arrayPool.Return(buffer);
            }
            context.WriteBuffer.WriteToBuffer(buffer.AsSpan(0, length));
        }
    }
}
