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
using RtmpSharp.Controller;

namespace RtmpSharp.Net
{
    public class RtmpServer : IDisposable
    {
        public int ReceiveTimeout { get; set; } = 10000;
        public int SendTimeout { get; set; } = 10000;
        public int PingPeriod { get; set; } = 10;
        public int PingTimeout { get; set; } = 10;
        public bool Started { get; private set; } = false;

        // TODO: add rtmps support

        Dictionary<string, ushort> _pathToPusherClientId = new Dictionary<string, ushort>();

        struct CrossClientConnection
        {
            public ushort PusherClientId { get; set; }
            public ushort PlayerClientId { get; set; }
            public ChannelType ChannelType { get; set; }
        }

        List<CrossClientConnection> _crossClientConnections = new List<CrossClientConnection>();
        Dictionary<string, Type> registeredApps = new Dictionary<string, Type>();

        internal IReadOnlyDictionary<string, Type> RegisteredApps
        {
            get
            {
                return registeredApps;
            }
        }
        List<ushort> allocated_stream_id = new List<ushort>();
        List<ushort> allocated_client_id = new List<ushort>();

        Random random = new Random();
        Socket listener = null;
        ManualResetEvent allDone = new ManualResetEvent(false);
        private SerializationContext context = null;
        private ObjectEncoding objectEncoding;
        private X509Certificate2 cert = null;
        private readonly int PROTOCOL_MIN_CSID = 3;
        private readonly int PROTOCOL_MAX_CSID = 65599;
        Dictionary<ushort, StreamConnectState> connects = new Dictionary<ushort, StreamConnectState>();
        List<KeyValuePair<ushort, StreamConnectState>> prepare_to_add = new List<KeyValuePair<ushort, StreamConnectState>>();
        List<ushort> prepare_to_remove = new List<ushort>();

        class StreamConnectState { public IStreamSession Connect; public DateTime LastPing; public Task ReaderTask; public Task WriterTask; }

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
                    server.EnabledSslProtocols = SslProtocols.None;
                }
                server.ListenerSocket.NoDelay = true;
                server.Start(socket =>
                {
                    socket.OnOpen = () =>
                    {
                        var path = socket.ConnectionInfo.Path.Split('/');
                        if (path.Length != 3) socket.Close();
                        ushort client_id = _getNewClientId();
                        IStreamSession connect = new WebsocketSession(socket, context, object_encoding);
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
                        catch { CloseClient(client_id); }

                    };
                    socket.OnPing = b => socket.SendPong(b);
                });
            }

            if (publishParameterAuth != null) this._publishParameterAuth = publishParameterAuth;
            if (playParameterAuth != null) this._playParameterAuth = playParameterAuth;

            listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.NoDelay = true;
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Parse(bindIp), bindRtmpPort);
            listener.Bind(localEndPoint);
            listener.Listen(10);
        }

        private void _ioLoop(CancellationToken ct)
        {
            try
            {
                while (Started)
                {
                    foreach (var current in connects)
                    {
                        ct.ThrowIfCancellationRequested();
                        StreamConnectState state = current.Value;
                        ushort client_id = current.Key;
                        IStreamSession connect = state.Connect;
                        if (connect.IsDisconnected)
                        {
                            CloseClient(client_id);
                            continue;
                        }
                        try
                        {
                            if (state.WriterTask == null || state.WriterTask.IsCompleted)
                            {
                                state.WriterTask = connect.WriteOnceAsync(ct);
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
                                state.ReaderTask = connect.ReadOnceAsync(ct);
                            }
                            if (state.ReaderTask.IsCanceled || state.ReaderTask.IsFaulted)
                            {
                                throw state.ReaderTask.Exception;
                            }

                        }
                        catch
                        {
                            CloseClient(client_id);
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
                            connects.TryGetValue(current, out var connection);
                            connects.Remove(current);
                            prepare_to_remove.RemoveAt(0);

                        }
                    }
                }
            }
            catch
            {

            }
        }

        public Task StartAsync(CancellationToken ct = default(CancellationToken))
        {
            if (Started)
            {
                throw new InvalidOperationException("already started");
            }
            Started = true;
            var ioThread = new Thread(() => _ioLoop(ct))
            {
                IsBackground = true
            };
            var ret = new TaskCompletionSource<int>();
            var t = new Thread(o =>
            {
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        try
                        {
                            allDone.Reset();
                            Console.WriteLine("Waiting for a connection...");
                            listener.BeginAccept(new AsyncCallback(ar =>
                            {
                                _acceptCallback(ar, ct);
                            }), listener);
                            while (!allDone.WaitOne(1))
                            {
                                ct.ThrowIfCancellationRequested();
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.ToString());
                        }
                    }
                }
                catch (OperationCanceledException) { }
                finally
                {
                    _clearConnections();
                    ioThread.Join();
                    ret.SetResult(1);
                }
            });


            ioThread.Start();
            t.Start();
            return ret.Task;
        }

        private void _clearConnections()
        {
            Started = false;
            foreach (var current in connects)
            {
                StreamConnectState state = current.Value;
                ushort client_id = current.Key;
                IStreamSession connect = state.Connect;
                if (connect.IsDisconnected)
                {
                    continue;
                }
                try
                {
                    CloseClient(client_id);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.StackTrace);
                }
            }
            connects.Clear();
            allocated_client_id.Clear();
            allocated_stream_id.Clear();
        }

        async void _acceptCallback(IAsyncResult ar, CancellationToken ct)
        {
            Socket listener = (Socket)ar.AsyncState;
            Socket handler = listener.EndAccept(ar);
            handler.NoDelay = true;
            // Signal the main thread to continue.
            allDone.Set();
            try
            {
                await _handshakeAsync(handler, ct);
            }
            catch (TimeoutException)
            {
                handler.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine("{0} Message: {1}", e.GetType().ToString(), e.Message);
                Console.WriteLine(e.StackTrace);
                handler.Close();
            }
        }

        private ushort _getUniqueIdOfList(IList<ushort> list, int min_value, int max_value)
        {
            ushort id;
            do
            {
                id = (ushort)random.Next(min_value, max_value);
            } while (list.IndexOf(id) != -1);
            return id;
        }

        private ushort _getUniqueIdOfList(IList<ushort> list)
        {
            ushort id;
            do
            {
                id = (ushort)random.Next();
            } while (list.IndexOf(id) != -1);
            return id;
        }

        internal ushort RequestStreamId()
        {
            return _getUniqueIdOfList(allocated_stream_id, PROTOCOL_MIN_CSID, PROTOCOL_MAX_CSID);
        }

        private ushort _getNewClientId()
        {
            return _getUniqueIdOfList(allocated_client_id);
        }

        private async Task<int> _handshakeAsync(Socket client_socket, CancellationToken ct)
        {
            Stream stream;
            if (cert != null)
            {
                var temp_stream = new SslStream(new NetworkStream(client_socket));
                try
                {
                    var op = new SslServerAuthenticationOptions();
                    op.ServerCertificate = cert;
                    await temp_stream.AuthenticateAsServerAsync(op, ct);
                }
                finally
                {
                    temp_stream.Close();
                }
                stream = temp_stream;
            }
            else
            {
                stream = new NetworkStream(client_socket);
            }
            var randomBytes = new byte[HandshakeRandomSize];
            random.NextBytes(randomBytes);
            client_socket.NoDelay = true;
            var s0s1 = new Handshake()
            {
                Version = 3,
                Time = 0,
                Time2 = 0,
                Random = randomBytes
            };
            using (var cts = new CancellationTokenSource())
            {
                using (var newCt = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ct))
                {
                    // read c0 + c1
                    var timer = new Timer((s) => { cts.Cancel(); }, null, ReceiveTimeout, Timeout.Infinite);
                    var c1 = await Handshake.ReadAsync(stream, true, newCt.Token);
                    timer.Change(Timeout.Infinite, Timeout.Infinite);

                    // write s0 + s1
                    timer.Change(SendTimeout, Timeout.Infinite);
                    await Handshake.WriteAsync(stream, s0s1, true, newCt.Token);
                    timer.Change(Timeout.Infinite, Timeout.Infinite);

                    // write s2
                    var s2 = c1;
                    timer.Change(SendTimeout, Timeout.Infinite);
                    await Handshake.WriteAsync(stream, s2, false, newCt.Token);
                    timer.Change(Timeout.Infinite, Timeout.Infinite);

                    // read c2
                    timer.Change(ReceiveTimeout, Timeout.Infinite);
                    var c2 = await Handshake.ReadAsync(stream, false, newCt.Token);
                    timer.Change(Timeout.Infinite, Timeout.Infinite);

                    // handshake check
                    if (!c2.Random.SequenceEqual(s0s1.Random))
                        throw new ProtocolViolationException();
                }
            }

            ushort client_id = _getNewClientId();
            var connect = new RtmpSession(client_socket, stream, this, client_id, context, objectEncoding, true);
            connect.ChannelDataReceived += _sendDataHandler;

            prepare_to_add.Add(new KeyValuePair<ushort, StreamConnectState>(client_id, new StreamConnectState()
            {
                Connect = connect,
                LastPing = DateTime.UtcNow,
                ReaderTask = null,
                WriterTask = null
            }));

            return client_id;
        }

        public void RegisterController<T>() where T : AbstractController
        {
            lock (registeredApps)
            {
                var typeT = typeof(T);
                var controllerName = typeT.Name;
                if (controllerName.EndsWith("Controller"))
                {
                    controllerName = controllerName.Substring(0, controllerName.LastIndexOf("Controller"));
                }

                if (registeredApps.ContainsKey(controllerName)) throw new InvalidOperationException("controller exists");
                registeredApps.Add(controllerName, typeT);
            }
        }

        public void CloseClient(ushort client_id)
        {
            allocated_client_id.Remove(client_id);
            allocated_stream_id.Remove(client_id);

            StreamConnectState state;
            connects.TryGetValue(client_id, out state);
            IStreamSession connect = state.Connect;

            prepare_to_remove.Add(client_id);

            if (connect.IsPublishing) UnRegisterPublish(client_id);
            if (connect.IsPlaying)
            {
                var client_channels = _crossClientConnections.FindAll(con => (con.PusherClientId == client_id || con.PlayerClientId == client_id));
                _crossClientConnections.RemoveAll(t => (t.PlayerClientId == client_id));
                foreach (var i in client_channels)
                {
                    _crossClientConnections.Remove(i);
                }

            }
            connect.Disconnect(new ExceptionalEventArgs("disconnected"));

        }

        private void _sendDataHandler(object sender, ChannelDataReceivedEventArgs e)
        {
            var server = (RtmpSession)sender;

            var server_clients = _crossClientConnections.FindAll((t) => t.PusherClientId == server.ClientId);
            foreach (var i in server_clients)
            {
                IStreamSession client;
                StreamConnectState client_state = null;
                if (e.type != i.ChannelType)
                {
                    continue;
                }
                connects.TryGetValue(i.PlayerClientId, out client_state);

                switch (i.ChannelType)
                {
                    case ChannelType.Video:
                    case ChannelType.Audio:
                        if (client_state == null) continue;
                        client = client_state.Connect;
                        client.SendAmf0Data(e.e);
                        break;
                    case ChannelType.Message:
                        throw new NotImplementedException();
                }

            }
        }

        internal void ConnectToClient(string app, string path, ushort playerClientId, ChannelType channelType)
        {
            StreamConnectState state;
            ushort pusherClientId;
            if (!_pathToPusherClientId.TryGetValue(path, out pusherClientId)) throw new KeyNotFoundException("Request Path Not Found");
            if (!connects.TryGetValue(pusherClientId, out state))
            {
                IStreamSession connect = state.Connect;
                _pathToPusherClientId.Remove(path);
                throw new KeyNotFoundException("Request Client Not Exists");
            }

            _crossClientConnections.Add(new CrossClientConnection()
            {
                PlayerClientId = playerClientId,
                PusherClientId = pusherClientId,
                ChannelType = channelType
            });

        }

        internal void SendMetadata(string app, string path, IStreamSession self, bool flvHeader = false)
        {
            ushort client_id;
            StreamConnectState state;
            IStreamSession connect;
            if (!_pathToPusherClientId.TryGetValue(path, out client_id)) throw new KeyNotFoundException("Request Path Not Found");
            if (!connects.TryGetValue(client_id, out state))
            {
                _pathToPusherClientId.Remove(path);
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

        internal async Task<bool> RegisterPublish(string app, string path, ushort clientId)
        {
            //var uri = new Uri("http://127.0.0.1/" + path);
            if (_pathToPusherClientId.ContainsKey(path)) return false;
            var ret = await _publishParameterAuth(app, HttpUtility.ParseQueryString(path));
            if (ret) _pathToPusherClientId.Add(path, clientId);
            return ret;
        }

        internal bool UnRegisterPublish(ushort clientId)
        {
            var key = _pathToPusherClientId.First(x => x.Value == clientId).Key;
            StreamConnectState state;
            if (_pathToPusherClientId.ContainsKey(key))
            {
                if (connects.TryGetValue(clientId, out state))
                {
                    IStreamSession connect = state.Connect;
                    connect.ChannelDataReceived -= _sendDataHandler;

                    var clients = _crossClientConnections.FindAll(t => t.PusherClientId == clientId);
                    foreach (var i in clients)
                    {
                        CloseClient(i.PlayerClientId);
                    }
                    _crossClientConnections.RemoveAll(t => t.PusherClientId == clientId);
                }
                _pathToPusherClientId.Remove(key);
                return true;
            }
            return false;
        }

        public delegate Task<bool> ParameterAuthCallback(string app, NameValueCollection collection);
        private ParameterAuthCallback _publishParameterAuth = async (a, n) => true;
        private ParameterAuthCallback _playParameterAuth = async (a, n) => true;

        internal bool AuthApp(string app, ushort client_id)
        {
            return registeredApps.ContainsKey(app);
        }

        public void Dispose()
        {
            try
            {
                if (Started)
                {
                    _clearConnections();
                    listener.Close();
                }
            }
            catch { }
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