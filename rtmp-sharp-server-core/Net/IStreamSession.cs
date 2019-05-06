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
    public interface IStreamSession
    {
        bool IsDisconnected { get; }
        ushort StreamId { get; }
        bool IsPublishing { get; }
        bool IsPlaying { get; }
        NotifyAmf0 FlvMetaData { get; }
        event ChannelDataReceivedEventHandler ChannelDataReceived;
        VideoData AVCConfigureRecord { get; set; }
        AudioData AACConfigureRecord { get; set; }
        Dictionary<string, dynamic> SessionStorage { get; }
        Queue<AudioData> AudioBuffer { get; }
        Queue<VideoData> VideoBuffer { get; }
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
