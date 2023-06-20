using Harmonic.Networking.Rtmp;
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Fleck;
using Harmonic.Networking.WebSocket;

namespace Harmonic.Hosting;

public class RtmpServer
{
    private readonly Socket _listener;
    private readonly ManualResetEvent _allDone = new(false);
    private readonly RtmpServerOptions _options;
    private WebSocketServer? _webSocketServer;
    private readonly WebSocketOptions? _webSocketOptions;

    public bool Started { get; private set; } = false;

    internal RtmpServer(RtmpServerOptions options, WebSocketOptions? webSocketOptions)
    {
        _options = options;
        _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _listener.NoDelay = true;
        _listener.Bind(options.RtmpEndPoint);
        _listener.Listen(128);
        _webSocketOptions = webSocketOptions;
        if (webSocketOptions != null)
        {
            _webSocketServer = new WebSocketServer($"{(options.Cert is null ? "ws" : "wss")}://{webSocketOptions.BindEndPoint}");
                
        }

    }
    public Task StartAsync(CancellationToken ct = default)
    {
        if (Started)
        {
            throw new InvalidOperationException("already started");
        }
        _webSocketServer?.Start(c =>
        {
            var session = new WebSocketSession(c, _webSocketOptions);
            c.OnOpen = session.HandleOpen;
            c.OnClose = session.HandleClose;
            c.OnMessage = session.HandleMessage;
        });

        if (_webSocketServer != null)
        {
            CancellationTokenRegistration reg = default;
            reg = ct.Register(() =>
            {
                reg.Dispose();
                _webSocketServer.Dispose();
                _webSocketServer = new WebSocketServer(_webSocketOptions?.BindEndPoint.ToString());
            });
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
                        _allDone.Reset();
                        _listener.BeginAccept(new AsyncCallback(ar =>
                        {
                            AcceptCallback(ar, ct);
                        }), _listener);
                        while (!_allDone.WaitOne(1))
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

        t.Start();
        return ret.Task;
    }
    private async void AcceptCallback(IAsyncResult ar, CancellationToken ct)
    {
        if(ar.AsyncState is Socket listener)
        {
            Socket client = listener.EndAccept(ar);
            await HandleClientAsync(client, ct);    
        }
        
    }

    private async Task HandleClientAsync(Socket client, CancellationToken ct)
    {
        client.NoDelay = true;
        // Signal the main thread to continue.
        _allDone.Set();
        IOPipeLine pipe = new IOPipeLine(client, _options);
        try
        {
            await pipe.StartAsync(ct);
        }
        catch (TaskCanceledException)
        {
            client.Close();
        }
        catch (TimeoutException)
        {
            client.Close();
        }
        catch (Exception e)
        {
            Console.WriteLine("{0} Message: {1}", e.GetType(), e.Message);
            Console.WriteLine(e.StackTrace);
            client.Close();
        }
        finally
        {
            pipe.Dispose();
        }
    }
}