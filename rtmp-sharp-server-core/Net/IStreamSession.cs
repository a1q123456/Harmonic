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
        event EventHandler Disconnected;
        bool IsDisconnected { get; }
        ushort StreamId { get; }
        bool IsPublishing { get; }
        bool IsPlaying { get; }
        NotifyAmf0 FlvMetaData { get; }
        ushort ClientId { get; }
        event ChannelDataReceivedEventHandler ChannelDataReceived;
        dynamic SessionStorage { get; set; }
        RtmpServer Server { get; }
        Task SendAmf0DataAsync(RtmpEvent e, CancellationToken ct = default);
        void WriteOnce();
        void ReadOnce();
        Task PingAsync(int pingTimeout);
        void Disconnect(ExceptionalEventArgs exceptionalEventArgs);
        void SendRawData(byte[] data);
        Task StartReadAsync(CancellationToken ct = default);
        Task WriteOnceAsync(CancellationToken ct = default);
        Task WriteProtocolControlMessage(RtmpEvent @event, CancellationToken ct = default);
        Task<T> InvokeAsync<T>(string endpoint, string destination, string method, object[] arguments);
        Task NotifyStatusAsync(AsObject status, CancellationToken ct = default);
    }
}
