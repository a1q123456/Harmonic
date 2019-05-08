using RtmpSharp.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Complete;
using RtmpSharp.Messaging.Events;
using System.Threading;
using RtmpSharp.IO;

namespace RtmpSharp.Net
{
    public interface IStreamSession
    {
        bool IsDisconnected { get; }
        ushort StreamId { get; }
        bool IsPublishing { get; }
        bool IsPlaying { get; }
        NotifyAmf0 FlvMetaData { get; }
        ushort ClientId { get; }
        event ChannelDataReceivedEventHandler ChannelDataReceived;
        dynamic SessionStorage { get; set; }
        void SendAmf0Data(RtmpEvent e);
        void WriteOnce();
        void ReadOnce();
        Task PingAsync(int pingTimeout);
        void Disconnect(ExceptionalEventArgs exceptionalEventArgs);
        void SendRawData(byte[] data);
        Task StartReadAsync(CancellationToken ct);
        Task WriteOnceAsync(CancellationToken ct);
        void WriteProtocolControlMessage(RtmpEvent @event);
        Task<T> InvokeAsync<T>(string endpoint, string destination, string method, object[] arguments);
        void NotifyStatus(AsObject status);
    }
}
