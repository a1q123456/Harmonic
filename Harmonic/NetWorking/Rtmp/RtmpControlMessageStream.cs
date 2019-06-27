using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Harmonic.NetWorking.Rtmp.Data;
using Harmonic.NetWorking.Rtmp.Messages;

namespace Harmonic.NetWorking.Rtmp
{
    public class RtmpControlMessageStream : RtmpMessageStream
    {
        private static readonly uint CONTROL_MSID = 0;

        internal RtmpControlMessageStream(RtmpSession rtmpStream) : base(rtmpStream, CONTROL_MSID)
        {
            RegisterMessageHandler<SetChunkSizeMessage>(MessageType.SetChunkSize, SetChunkSize);
            RegisterMessageHandler<WindowAcknowledgementSizeMessage>(MessageType.WindowAcknowledgementSize, WindowAcknowledgementSize);
            RegisterMessageHandler<SetPeerBandwidthMessage>(MessageType.SetPeerBandwidth, SetPeerBandwidth);
        }

        private void SetPeerBandwidth(SetPeerBandwidthMessage message)
        {
            RtmpSession.RtmpStream.ReadWindowAcknowledgementSize = message.WindowSize;
            SendControlMessageAsync(new AcknowledgementMessage()
            {
                BytesReceived = RtmpSession.RtmpStream.ReadWindowSize
            });
            RtmpSession.RtmpStream.ReadWindowSize = 0;
            RtmpSession.RtmpStream.BandwidthLimited = true;
        }

        private void WindowAcknowledgementSize(WindowAcknowledgementSizeMessage message)
        {
            RtmpSession.RtmpStream.ReadWindowAcknowledgementSize = message.WindowSize;
            SendControlMessageAsync(new AcknowledgementMessage()
            {
                BytesReceived = RtmpSession.RtmpStream.ReadWindowSize
            });
            RtmpSession.RtmpStream.ReadWindowSize = 0;
        }


        private void SetChunkSize(SetChunkSizeMessage setChunkSize)
        {
            RtmpSession.RtmpStream.ReadChunkSize = (int)setChunkSize.ChunkSize;
        }

        public Task SendControlMessageAsync(Message message)
        {
            if (message.MessageHeader.MessageType == MessageType.WindowAcknowledgementSize)
            {
                RtmpSession.RtmpStream.WriteWindowAcknowledgementSize = ((WindowAcknowledgementSizeMessage)message).WindowSize;
            }
            return SendMessageAsync(RtmpSession.ControlChunkStream, message);
        }
    }
}
