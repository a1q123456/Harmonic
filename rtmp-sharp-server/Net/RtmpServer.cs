using Complete;
using RtmpSharp.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Collections.Specialized;
using System.Collections.Concurrent;
using RtmpSharp.Messaging;
using Fleck;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Security.Authentication;

namespace RtmpSharp.Net
{
    public class RtmpServer
    {
        public int ReceiveTimeout = 10000;
        public int SendTimeout = 10000;

        public int PingPeriod = 10;
        public int PingTimeout = 10;
        public bool Started { get; private set; } = false;

        // TODO: add rtmps support

        Dictionary<Tuple<string, string>, ushort> clientRoute = new Dictionary<Tuple<string, string>, ushort>();
        List<Tuple<ushort, ushort, ChannelType>> routedClients = new List<Tuple<ushort, ushort, ChannelType>>();
        List<string> registeredApps = new List<string>();
        List<ushort> allocated_stream_id = new List<ushort>();
        List<ushort> allocated_client_id = new List<ushort>();
        
        Random random = new Random();
        Thread ioThread;
        Socket listener;
        ManualResetEvent allDone = new ManualResetEvent(false);
        private SerializationContext context;
        private ObjectEncoding objectEncoding;
        private X509Certificate2 cert = null;
        private readonly int PROTOCOL_MIN_CSID = 3;
        private readonly int PROTOCOL_MAX_CSID = 65599;
        Dictionary<ushort, StreamConnectState> connects = new Dictionary<ushort, StreamConnectState>();
        List<KeyValuePair<ushort, StreamConnectState>> prepare_to_add = new List<KeyValuePair<ushort, StreamConnectState>>();
        List<ushort> prepare_to_remove = new List<ushort>();

        class StreamConnectState { public IStreamConnect Connect; public DateTime LastPing; public Task ReaderTask; public Task WriterTask; }

        public RtmpServer(
            SerializationContext context,
            X509Certificate2 cert = null,
            ObjectEncoding object_encoding = ObjectEncoding.Amf0,
            ParameterAuthCallback publishParameterAuth = null, 
            ParameterAuthCallback playParameterAuth = null, 
            string bindIp = "0.0.0.0", 
            int bindRtmpPort = 1935,
            int bindWebsocketPort = -1
            )
        {
            this.context = context;
            objectEncoding = object_encoding;
            if (bindWebsocketPort != -1)
            {
                var server = new WebSocketServer("ws://" + bindIp.ToString() + ":" + bindWebsocketPort.ToString());
                if (cert != null)
                {
                    this.cert = cert;
                    server.Certificate = cert;
                    server.EnabledSslProtocols = SslProtocols.Default;
                }
                server.ListenerSocket.NoDelay = true;
                server.Start(socket =>
                {
                    socket.OnOpen = () =>
                    {
                        var path = socket.ConnectionInfo.Path.Split('/');
                        if (path.Length != 3) socket.Close();
                        ushort client_id = GetNewClientId();
                        IStreamConnect connect = new WebsocketConnect(socket, context, object_encoding);
                        lock (connects)
                        {
                            connects.Add(client_id, new StreamConnectState()
                            {
                                Connect = connect,
                                LastPing = DateTime.UtcNow,
                                ReaderTask = null,
                                WriterTask = null
                            });
                        }
                        try
                        {
                            SendMetadata(path[1], path[2], connect, flvHeader: true);

                            ConnectToClient(path[1], path[2], client_id, ChannelType.Audio);
                            ConnectToClient(path[1], path[2], client_id, ChannelType.Video);
                        }
                        catch { Close(client_id); }

                    };
                    socket.OnPing = b => socket.SendPong(b);
                });
            }
            
            if (publishParameterAuth != null) this.publishParameterAuth = publishParameterAuth;
            if (playParameterAuth != null) this.playParameterAuth = playParameterAuth;
            ioThread = new Thread(() =>
            {
                while (true)
                {
                    foreach (var current in connects)
                    {
                        StreamConnectState state = current.Value;
                        ushort client_id = current.Key;
                        IStreamConnect connect = state.Connect;
                        if (connect.IsDisconnected)
                        {
                            Close(client_id);
                            continue;
                        }
                        try
                        {
                            if (state.WriterTask == null || state.WriterTask.IsCompleted)
                            {
                                state.WriterTask = connect.WriteOnceAsync();
                            }
                            if (state.WriterTask.IsCanceled || state.WriterTask.IsFaulted)
                            {
                                throw state.WriterTask.Exception;
                            }
                            if (state.LastPing == null || DateTime.UtcNow - state.LastPing >= new TimeSpan(0, 0, PingPeriod))
                            {
                                connect.PingAsync(PingTimeout);
                                state.LastPing = DateTime.UtcNow;
                            }


                            if (state.ReaderTask == null || state.ReaderTask.IsCompleted)
                            {
                                state.ReaderTask = connect.ReadOnceAsync();
                            }
                            if (state.ReaderTask.IsCanceled || state.ReaderTask.IsFaulted)
                            {
                                throw state.ReaderTask.Exception;
                            }

                        }
                        catch
                        {
                            Close(client_id);
                            continue;
                        }
                    }
                    var prepare_add_length = prepare_to_add.Count;
                    if (prepare_add_length != 0)
                    {
                        for (int i = 0; i < prepare_add_length; i++)
                        {
                            var current = prepare_to_add[0];
                            connects.Add(current.Key, current.Value);
                            prepare_to_add.RemoveAt(0);
                        }
                    }
                    

                    var prepare_remove_length = prepare_to_remove.Count;
                    if (prepare_remove_length != 0)
                    {
                        for (int i = 0; i < prepare_remove_length; i++)
                        {
                            var current = prepare_to_remove[0];
                            connects.Remove(current);
                            prepare_to_remove.RemoveAt(0);
                        }
                    }
                    
                }
            })
            { IsBackground = true };
            
            ioThread.Start();

            listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.NoDelay = true;
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Parse(bindIp), bindRtmpPort);
            listener.Bind(localEndPoint);
            listener.Listen(10);
        }
        
        public void Start()
        {
            Started = true;
            try
            {
                while (true)
                {
                    allDone.Reset();
                    Console.WriteLine("Waiting for a connection...");
                    listener.BeginAccept(new AsyncCallback(acceptCallback), listener);
                    allDone.WaitOne();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        public void StartAsync()
        {
            var t = new Thread(Start);
            t.Start();
        }

        public void Stop()
        {
            Started = false;
            foreach(var current in connects)
            {
                StreamConnectState state = current.Value;
                ushort client_id = current.Key;
                IStreamConnect connect = state.Connect;
                if (connect.IsDisconnected)
                {
                    continue;
                }
                try
                {
                    Close(client_id);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.StackTrace);
                }
            }
            connects.Clear();
            allocated_client_id.Clear();
            allocated_stream_id.Clear();
            listener.Close();
        }

        async void acceptCallback(IAsyncResult ar)
        {
            Socket listener = (Socket)ar.AsyncState;
            Socket handler = listener.EndAccept(ar);
            handler.NoDelay = true;
            // Signal the main thread to continue.
            allDone.Set();
            try
            {
                await HandshakeAsync(handler);
            }
            catch (TimeoutException)
            {
                handler.Close();
            }
            catch (AuthenticationException)
            {
                handler.Close();
                throw;
            }

        }

        private ushort GetUniqueIdOfList(IList<ushort> list, int min_value, int max_value)
        {
            ushort id;
            do
            {
                id = (ushort)random.Next(min_value, max_value);
            } while (list.IndexOf(id) != -1);
            return id;
        }

        private ushort GetUniqueIdOfList(IList<ushort> list)
        {
            ushort id;
            do
            {
                id = (ushort)random.Next();
            } while (list.IndexOf(id) != -1);
            return id;
        }

        public ushort RequestStreamId()
        {
            return GetUniqueIdOfList(allocated_stream_id, PROTOCOL_MIN_CSID, PROTOCOL_MAX_CSID);
        }

        private ushort GetNewClientId()
        {
            return GetUniqueIdOfList(allocated_client_id);
        }

        public async Task<int> HandshakeAsync(Socket client_socket)
        {
            Stream stream;
            if (cert != null)
            {
                var temp_stream = new SslStream(new NetworkStream(client_socket));
                try
                {
                    await temp_stream.AuthenticateAsServerAsync(cert);
                }
                catch (AuthenticationException)
                {
                    temp_stream.Close();
                    throw;
                }
                stream = temp_stream;
            }
            else
            {
                stream = new NetworkStream(client_socket);
            }
            var randomBytes = new byte[1528];
            random.NextBytes(randomBytes);
            client_socket.NoDelay = true;
            var s01 = new Handshake()
            {
                Version = 3,
                Time = (uint)Environment.TickCount,
                Time2 = 0,
                Random = randomBytes
            };
            CancellationTokenSource cts = new CancellationTokenSource();

            Timer timer = new Timer((s) => { cts.Cancel(); throw new TimeoutException(); }, null, ReceiveTimeout, Timeout.Infinite);
            var c01 = await Handshake.ReadAsync(stream, true, cts.Token);
            timer.Change(Timeout.Infinite, Timeout.Infinite);

            timer.Change(ReceiveTimeout, Timeout.Infinite);
            await Handshake.WriteAsync(stream, s01, true, cts.Token);
            timer.Change(Timeout.Infinite, Timeout.Infinite);
            // read c2

            timer.Change(SendTimeout, Timeout.Infinite);
            var c2 = await Handshake.ReadAsync(stream, false, cts.Token);
            timer.Change(Timeout.Infinite, Timeout.Infinite);

            // write s2
            var s2 = c01.Clone();
            // s2.Time2 = (uint)Environment.TickCount;


            timer.Change(ReceiveTimeout, Timeout.Infinite);
            await Handshake.WriteAsync(stream, s2, false, cts.Token);
            timer.Change(Timeout.Infinite, Timeout.Infinite);

            // handshake check
            if (!c01.Random.SequenceEqual(s2.Random))
                throw new ProtocolViolationException();


            ushort client_id = GetNewClientId();
            var connect = new RtmpConnect(client_socket, stream, this, client_id, context, objectEncoding, true);
            connect.ChannelDataReceived += sendDataHandler;

            prepare_to_add.Add(new KeyValuePair<ushort, StreamConnectState>(client_id, new StreamConnectState() {
                Connect = connect,
                LastPing = DateTime.UtcNow,
                ReaderTask = null,
                WriterTask = null
            }));
            
            return client_id;
        }

        public void RegisterApp(string app_name)
        {
            lock (registeredApps)
            {
                if (registeredApps.IndexOf(app_name) != -1) throw new InvalidOperationException("app exists");
                registeredApps.Add(app_name);
            }
        }

        public void Close(ushort client_id)
        {
            allocated_client_id.Remove(client_id);
            allocated_stream_id.Remove(client_id);

            StreamConnectState state;
            connects.TryGetValue(client_id, out state);
            IStreamConnect connect = state.Connect;

            prepare_to_remove.Add(client_id);
            
            if (connect.IsPublishing) UnRegisterPublish(client_id);
            if (connect.IsPlaying)
            {
                var client_channels = routedClients.FindAll((t) => (t.Item1 == client_id || t.Item2 == client_id));
                routedClients.RemoveAll((t) => (t.Item1 == client_id));
                foreach (var i in client_channels)
                {
                    routedClients.Remove(i);
                }
                
            }
            connect.OnDisconnected(new ExceptionalEventArgs("disconnected"));
            
        }

        private void sendDataHandler(object sender, ChannelDataReceivedEventArgs e)
        {
            var server = (RtmpConnect)sender;

            var server_clients = routedClients.FindAll((t) => t.Item2 == server.ClientId);
            foreach (var i in server_clients)
            {
                IStreamConnect client;
                StreamConnectState client_state = null;
                if (e.type != i.Item3)
                {
                    continue;
                }
                connects.TryGetValue(i.Item1, out client_state);

                switch (i.Item3)
                {
                    case ChannelType.Audio:
                        if (client_state == null) continue;
                        client = client_state.Connect;
                        client.SendAmf0Data(e.e);
                        break;
                    case ChannelType.Video:
                        if (client_state == null) continue;
                        client = client_state.Connect;
                        client.SendAmf0Data(e.e);
                        break;
                    case ChannelType.Message:
                        throw new NotImplementedException();
                }
                
            }
        }
        
        internal void ConnectToClient(string app, string path, ushort self_id, ChannelType channel_type)
        {
            StreamConnectState state;
            ushort client_id;
            var uri = new Uri("http://127.0.0.1/" + path);
            var key = new Tuple<string, string>(app, uri.AbsolutePath);
            if (!clientRoute.TryGetValue(key, out client_id)) throw new KeyNotFoundException("Request Path Not Found");
            if (!connects.TryGetValue(client_id, out state))
            {
                IStreamConnect connect = state.Connect;
                clientRoute.Remove(key);
                throw new KeyNotFoundException("Request Client Not Exists");
            }

            routedClients.Add(new Tuple<ushort, ushort, ChannelType>(self_id, client_id, channel_type));
            
        }

        internal void SendMetadata(string app, string path, IStreamConnect self, bool flvHeader = false)
        {
            ushort client_id;
            StreamConnectState state;
            IStreamConnect connect;
            var uri = new Uri("http://127.0.0.1/" + path);
            var key = new Tuple<string, string>(app, uri.AbsolutePath);
            if (!clientRoute.TryGetValue(key, out client_id)) throw new KeyNotFoundException("Request Path Not Found");
            if (!connects.TryGetValue(client_id, out state))
            {
                clientRoute.Remove(key);
                throw new KeyNotFoundException("Request Client Not Exists");
            }
            connect = state.Connect;
            if (connect.IsPublishing)
            {
                var flv_metadata = (Dictionary<string, object>)connect.FlvMetaData.MethodCall.Parameters[0];
                var has_audio = flv_metadata.ContainsKey("audiocodecid");
                var has_video = flv_metadata.ContainsKey("videocodecid");
                if (flvHeader)
                {
                    var header_buffer = Enumerable.Repeat<byte>(0x00, 13).ToArray<byte>();
                    header_buffer[0] = 0x46;
                    header_buffer[1] = 0x4C;
                    header_buffer[2] = 0x56;
                    header_buffer[3] = 0x01;
                    byte has_audio_flag = 0x01 << 2;
                    byte has_video_flag = 0x01;
                    byte type_flag = 0x00;
                    if (has_audio) type_flag |= has_audio_flag;
                    if (has_video) type_flag |= has_video_flag;
                    header_buffer[4] = type_flag;
                    var data_offset = BitConverter.GetBytes((uint)9);
                    header_buffer[5] = data_offset[3];
                    header_buffer[6] = data_offset[2];
                    header_buffer[7] = data_offset[1];
                    header_buffer[8] = data_offset[0];
                    self.SendRawData(header_buffer);
                }
                self.SendAmf0Data(connect.FlvMetaData);
                if (has_audio) self.SendAmf0Data(connect.AACConfigureRecord);
                if (has_video) self.SendAmf0Data(connect.AvCConfigureRecord);
            }
        }

        internal bool RegisterPublish(string app, string path, ushort clientId)
        {
            var uri = new Uri("http://127.0.0.1/" + path);
            var key = new Tuple<string, string>(app, uri.AbsolutePath);
            if (clientRoute.ContainsKey(key)) return false;
            var ret = publishParameterAuth(app, HttpUtility.ParseQueryString(uri.Query));
            if (ret) clientRoute.Add(key, clientId);
            return ret;
        }

        internal bool UnRegisterPublish(ushort clientId)
        {
            var key = clientRoute.First(x => x.Value == clientId).Key;
            StreamConnectState state;
            if (clientRoute.ContainsKey(key))
            {
                if (connects.TryGetValue(clientId, out state))
                {
                    IStreamConnect connect = state.Connect;
                    connect.ChannelDataReceived -= sendDataHandler;

                    var clients = routedClients.FindAll(t => t.Item2 == clientId);
                    foreach (var i in clients)
                    {
                        Close(i.Item1);
                    }
                    routedClients.RemoveAll(t => t.Item2 == clientId);
                    
                }
                clientRoute.Remove(key);
                return true;
            }
            return false;
        }

        internal bool RegisterPlay(string app, string path, int clientId)
        {
            var uri = new Uri("http://127.0.0.1/" + path);
            return playParameterAuth(app, HttpUtility.ParseQueryString(uri.Query));
        }

        public delegate bool ParameterAuthCallback(string app, NameValueCollection collection);
        private ParameterAuthCallback publishParameterAuth = (a, n) => true;
        private ParameterAuthCallback playParameterAuth = (a, n) => true;

        internal bool AuthApp(string app, ushort client_id)
        {
            if (registeredApps.IndexOf(app) == -1) return false;
            return true;
        }

        #region handshake

        const int HandshakeRandomSize = 1528;

        // size for c0, c1, s1, s2 packets. c0 and s0 are 1 byte each.
        const int HandshakeSize = HandshakeRandomSize + 4 + 4;

        public struct Handshake
        {
            // C0/S0 only
            public byte Version;

            // C1/S1/C2/S2
            public uint Time;
            // in C1/S1, MUST be zero. in C2/S2, time at which C1/S1 was read.
            public uint Time2;
            public byte[] Random;

            public Handshake Clone()
            {
                return new Handshake()
                {
                    Version = Version,
                    Time = Time,
                    Time2 = Time2,
                    Random = Random
                };
            }

            public static async Task<Handshake> ReadAsync(Stream stream, bool readVersion, CancellationToken cancellationToken)
            {
                var size = HandshakeSize + (readVersion ? 1 : 0);
                var buffer = await StreamHelper.ReadBytesAsync(stream, size, cancellationToken);

                using (var reader = new AmfReader(new MemoryStream(buffer), null))
                {
                    return new Handshake()
                    {
                        Version = readVersion ? reader.ReadByte() : default(byte),
                        Time = reader.ReadUInt32(),
                        Time2 = reader.ReadUInt32(),
                        Random = reader.ReadBytes(HandshakeRandomSize)
                    };
                }
            }

            public static Task WriteAsync(Stream stream, Handshake h, bool writeVersion, CancellationToken ct)
            {
                using (var memoryStream = new MemoryStream())
                using (var writer = new AmfWriter(memoryStream, null))
                {
                    if (writeVersion)
                        writer.WriteByte(h.Version);

                    writer.WriteUInt32(h.Time);
                    writer.WriteUInt32(h.Time2);
                    writer.WriteBytes(h.Random);

                    var buffer = memoryStream.ToArray();
                    return stream.WriteAsync(buffer, 0, buffer.Length, ct);
                }
            }
        }



        #endregion
    }
}