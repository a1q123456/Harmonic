using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Complete;
using RtmpSharp.Messaging;
using Complete.Threading;
using Fleck;
using System.Threading;
using System.IO;
using RtmpSharp.Messaging.Events;
using RtmpSharp.IO;

namespace RtmpSharp.Net
{
    class WebsocketConnect : IStreamConnect, IDisposable
    {
        public NotifyAmf0 FlvMetaData
        {
            get
            {
                throw new InvalidOperationException("websocket client not support publish yet");
            }
        }


        public bool IsPlaying { get; private set; } = false;

        public bool IsPublishing { get; } = false;

        public ushort StreamId { get; private set; }

        public event ChannelDataReceivedEventHandler ChannelDataReceived;
        public event EventHandler<Exception> CallbackException;
        public event EventHandler Disconnected;

        public delegate Task SendPingDelegate(byte[] data);

        private SendPingDelegate sendPing;
        private DateTime connectTime;
        private TaskCallbackManager<int, object> callbackManager = new TaskCallbackManager<int, object>();
        private Queue<RtmpEvent> writeQueue = new Queue<RtmpEvent>();
        private AutoResetEvent packetAvailableEvent = new AutoResetEvent(false);
        private FlvPacketWriter writer;
        private RtmpPacketReader reader;
        private IWebSocketConnection connection;
        public bool IsDisconnected => disconnectsFired != 0;

        public bool HasConnected { get; private set; }

        public VideoData AvCConfigureRecord
        {
            get
            {
                throw new NotSupportedException();
            }
        }

        public AudioData AACConfigureRecord
        {
            get
            {
                throw new NotSupportedException();
            }
        }

        private volatile int disconnectsFired = 0;

        public WebsocketConnect(IWebSocketConnection connection, IO.SerializationContext context, IO.ObjectEncoding encoding)
        {
            this.connection = connection;
            IsPlaying = true;
            sendPing = connection.SendPing;
            connectTime = DateTime.UtcNow;
            connection.OnPong += d => callbackManager.SetResult(BitConverter.ToInt32(d, 0), null);
            connection.OnClose += () =>
            {
                OnDisconnected(new ExceptionalEventArgs("Closed"));
            };
            connection.OnError += (e) =>
            {
                OnDisconnected(new ExceptionalEventArgs(e.Message, e));
            };
            var stream = new WebsocketStream(connection);
            writer = new FlvPacketWriter(new IO.AmfWriter(stream, context), encoding);
            reader = new RtmpPacketReader(new IO.AmfReader(stream, context));
        }
        public void OnDisconnected(ExceptionalEventArgs e)
        {
            if (Interlocked.Increment(ref disconnectsFired) > 1)
                return;

            HasConnected = false;
            WrapCallback(() => Disconnected?.Invoke(this, e));
            WrapCallback(() => callbackManager.SetExceptionForAll(new ClientDisconnectedException(e.Description, e.Exception)));
            Dispose(true);
        }

        private void WrapCallback(Action action)
        {
            try
            {
                try { action(); }
                catch (Exception ex) { CallbackException?.Invoke(this, ex); throw ex; }
            }
#if DEBUG 
            catch (Exception unhandled)
            {
                System.Diagnostics.Debug.Print("UNHANDLED EXCEPTION IN CALLBACK: {0}: {1} @ {2}", unhandled.GetType(), unhandled.Message, unhandled.StackTrace);
                System.Diagnostics.Debugger.Break();
            }
#else
            catch { }
#endif
        }

        public Task PingAsync(int pingTimeout)
        {
            var timestamp = (int)(DateTime.UtcNow - connectTime).TotalSeconds;
            sendPing(BitConverter.GetBytes(timestamp));
            return callbackManager.Create(timestamp);
        }

        public bool ReadOnce()
        {
            return reader.ReadOnce();
        }

        public Task ReadOnceAsync()
        {
            return null;
        }

        public Task WriteOnceAsync()
        {
            return null;
        }

        public void SendAmf0Data(RtmpEvent e)
        {
            var timestamp = (int)(DateTime.UtcNow - connectTime).TotalMilliseconds;
            e.Timestamp = timestamp;
            writer.Queue(e, 3, 1337);
        }

        public bool WriteOnce()
        {
            if (IsDisconnected) throw new IOException("Websocket already closed");
            return writer.WriteOnce();
        }

        public void SendRawData(byte[] data)
        {
            connection.Send(data);
        }

        #region IDisposable Support
        private bool disposedValue = false; // 要检测冗余调用

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    connection.Close();
                }

                // TODO: 释放未托管的资源(未托管的对象)并在以下内容中替代终结器。
                // TODO: 将大型字段设置为 null。

                disposedValue = true;
            }
        }

        // TODO: 仅当以上 Dispose(bool disposing) 拥有用于释放未托管资源的代码时才替代终结器。
        // ~WebsocketConnect() {
        //   // 请勿更改此代码。将清理代码放入以上 Dispose(bool disposing) 中。
        //   Dispose(false);
        // }

        // 添加此代码以正确实现可处置模式。
        public void Dispose()
        {
            // 请勿更改此代码。将清理代码放入以上 Dispose(bool disposing) 中。
            Dispose(true);
            // TODO: 如果在以上内容中替代了终结器，则取消注释以下行。
            // GC.SuppressFinalize(this);
        }

        


        #endregion

    }
}
