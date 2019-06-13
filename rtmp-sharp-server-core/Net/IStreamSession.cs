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
        ushort ClientId { get; }
        dynamic SessionStorage { get; set; }
        RtmpServer Server { get; }
        void SendAmf0Data(RtmpEvent e);
        Task PingAsync(CancellationToken ct = default);
        void Disconnect(ExceptionalEventArgs exceptionalEventArgs);
        void SendRawData(byte[] data);
        void WriteProtocolControlMessage(RtmpEvent @event);
        Task<T> InvokeAsync<T>(string endpoint, string destination, string method, object[] arguments);
        void NotifyStatus(AsObject status);
    }
}
