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
using RtmpSharp.Net;
using Autofac;
using RtmpSharp.Service;
using System.Reflection;

namespace RtmpSharp.Hosting
{
    public class RtmpServer : IDisposable
    {
        public int ReceiveTimeout { get; set; } = 10000;
        public int SendTimeout { get; set; } = 10000;
        public int PingInterval { get; set; } = 10;
        public int PingTimeout { get; set; } = 10;
        public bool Started { get; private set; } = false;

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
        internal Dictionary<ushort, IStreamSession> connectedSessions = new Dictionary<ushort, IStreamSession>();
        private Supervisor supervisor = null;
        internal IContainer ServiceContainer { get; set; } = null;
        public ILifetimeScope ServerLifetime { get; set; } = null;
        internal List<Type> SessionScopedServices { get; set; } = null;
        public RtmpServer(
            IStartup serverStartUp,
            SerializationContext context,
            X509Certificate2 cert = null,
            ObjectEncoding object_encoding = ObjectEncoding.Amf0,
            string bindIp = "0.0.0.0",
            int bindRtmpPort = 1935,
            int bindWebsocketPort = -1
            )
        {
            this.context = context;
            objectEncoding = object_encoding;

            listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.NoDelay = true;
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Parse(bindIp), bindRtmpPort);
            listener.Bind(localEndPoint);
            listener.Listen(10);
            var builder = new ContainerBuilder();
            serverStartUp.ConfigureServices(builder);
            SessionScopedServices = new List<Type>(serverStartUp.SessionScopedServices);
            RegisterCommonServices(builder);
            ServiceContainer = builder.Build();
            ServerLifetime = ServiceContainer.BeginLifetimeScope();
        }

        public Task StartAsync(CancellationToken ct = default)
        {
            if (Started)
            {
                throw new InvalidOperationException("already started");
            }
            Started = true;
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
                                AcceptCallback(ar, ct);
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
                    ret.SetResult(1);
                }
            });

            supervisor = new Supervisor(this);
            supervisor.StartAsync(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), ct);
            t.Start();
            return ret.Task;
        }

        private void RegisterCommonServices(ContainerBuilder builder)
        {
            builder.Register(c => new PublisherSessionService())
                .AsSelf()
                .InstancePerLifetimeScope();

        }

        async void AcceptCallback(IAsyncResult ar, CancellationToken ct)
        {
            Socket listener = (Socket)ar.AsyncState;
            Socket handler = listener.EndAccept(ar);
            handler.NoDelay = true;
            // Signal the main thread to continue.
            allDone.Set();
            try
            {
                await HandshakeAsync(handler, ct);
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

        internal ushort RequestStreamId()
        {
            return GetUniqueIdOfList(allocated_stream_id, PROTOCOL_MIN_CSID, PROTOCOL_MAX_CSID);
        }

        private ushort GetNewClientId()
        {
            return GetUniqueIdOfList(allocated_client_id);
        }

        private async Task<int> HandshakeAsync(Socket clientSocket, CancellationToken ct)
        {
            Stream stream;
            if (cert != null)
            {
                var tempStream = new SslStream(new NetworkStream(clientSocket));
                try
                {
                    var op = new SslServerAuthenticationOptions();
                    op.ServerCertificate = cert;
                    await tempStream.AuthenticateAsServerAsync(op, ct);
                }
                finally
                {
                    tempStream.Close();
                }
                stream = tempStream;
            }
            else
            {
                stream = new NetworkStream(clientSocket);
            }
            var randomBytes = new byte[HandshakeRandomSize];
            random.NextBytes(randomBytes);
            clientSocket.NoDelay = true;
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

            ushort clientId = GetNewClientId();
            var session = new RtmpSession(clientSocket, stream, this, clientId, context, objectEncoding, true);

            connectedSessions.Add(clientId, session);
            session.Disconnected += (s, e) =>
            {
                connectedSessions.Remove(clientId);
            };

            return clientId;
        }
        public void RegisterController<T>(string appName) where T : AbstractController
        {
            if (appName.Contains('/'))
            {
                throw new ArgumentOutOfRangeException();
            }
            lock (registeredApps)
            {
                if (registeredApps.ContainsKey(appName)) throw new InvalidOperationException("app exists");
                registeredApps.Add(appName, typeof(T));
            }
        }
        public void RegisterController<T>() where T : AbstractController
        {
            RegisterController<T>(typeof(T).Name.Replace("Controller", "").ToLower());
        }

        internal bool AuthApp(string app)
        {
            return registeredApps.ContainsKey(app);
        }

        public void Dispose()
        {
            try
            {
                if (Started)
                {
                    listener.Close();
                }
                ServerLifetime.Dispose();
                ServiceContainer?.Dispose();
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
                        Version = readVersion ? reader.ReadByte() : default,
                        Time = reader.ReadUInt32(),
                        Time2 = reader.ReadUInt32(),
                        Random = reader.ReadBytes(HandshakeRandomSize)
                    };
                }
            }

            public static Task WriteAsync(Stream stream, Handshake h, bool writeVersion, CancellationToken ct)
            {
                using (var writer = new AmfWriter(null))
                {
                    if (writeVersion)
                        writer.WriteByte(h.Version);

                    writer.WriteUInt32(h.Time);
                    writer.WriteUInt32(h.Time2);
                    writer.WriteBytes(h.Random);

                    var buffer = writer.GetBytes();
                    return stream.WriteAsync(buffer, 0, buffer.Length, ct);
                }
            }
        }

        #endregion
    }
}