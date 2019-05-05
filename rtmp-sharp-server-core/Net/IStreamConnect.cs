using RtmpSharp.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Complete;
using RtmpSharp.Messaging.Events;
using System.Threading;

namespace RtmpSharp.Net
{
    interface IStreamConnect
    {
        bool IsDisconnected { get; }
        ushort StreamId { get; }
        bool IsPublishing { get; }
        bool IsPlaying { get; }
        NotifyAmf0 FlvMetaData { get; }
        event ChannelDataReceivedEventHandler ChannelDataReceived;
        VideoData AvCConfigureRecord { get; }
        AudioData AACConfigureRecord { get; }

        void SendAmf0Data(RtmpEvent e);

        void WriteOnce();
        void ReadOnce();
        Task PingAsync(int pingTimeout);
        void OnDisconnected(ExceptionalEventArgs exceptionalEventArgs);
        void SendRawData(byte[] data);
        Task ReadOnceAsync(CancellationToken ct);
        Task WriteOnceAsync(CancellationToken ct);
    }
}
