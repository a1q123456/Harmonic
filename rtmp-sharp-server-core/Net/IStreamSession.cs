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
using RtmpSharp.Hosting;

namespace RtmpSharp.Net
{
    public interface IStreamSession
    {
        event EventHandler Disconnected;
        ConnectionInformation ConnectionInformation { get; }
        bool IsDisconnected { get; }
        ushort StreamId { get; }
        bool IsPublishing { get; }
        bool IsPlaying { get; }
        NotifyAmf0 FlvMetaData { get; }
        ushort ClientId { get; }
        dynamic SessionStorage { get; set; }
        RtmpServer Server { get; }
        Task SendAmf0DataAsync(RtmpEvent e, CancellationToken ct = default);
        void WriteOnce();
        void ReadOnce();
        Task PingAsync(CancellationToken ct = default);
        void Disconnect(ExceptionalEventArgs exceptionalEventArgs);
        void SendRawData(byte[] data);
        Task StartReadAsync(CancellationToken ct = default);
        Task WriteOnceAsync(CancellationToken ct = default);
        Task WriteProtocolControlMessageAsync(RtmpEvent @event, CancellationToken ct = default);
        Task<T> InvokeAsync<T>(string endpoint, string destination, string method, object[] arguments);
        Task NotifyStatusAsync(AsObject status, CancellationToken ct = default);
    }
}
