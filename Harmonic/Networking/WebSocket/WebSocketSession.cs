using Autofac;
using Fleck;
using Harmonic.Controllers;
using Harmonic.Hosting;
using System;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Flv;
using System.Threading.Tasks;
using System.Web;

namespace Harmonic.Networking.WebSocket;

public class WebSocketSession
{
    private readonly IWebSocketConnection _webSocketConnection;
    private readonly WebSocketOptions? _options;
    private WebSocketController _controller;
    private readonly FlvMuxer _flvMuxer;
    public RtmpServerOptions Options => _options._serverOptions;

    public WebSocketSession(IWebSocketConnection connection, WebSocketOptions? options)
    {
        _webSocketConnection = connection;
        _options = options;
        _flvMuxer = new FlvMuxer();
    }

    public Task SendRawDataAsync(byte[] data)
    {
        return _webSocketConnection.Send(data);
    }

    public void Close()
    {
        _webSocketConnection.Close();
    }

    public void SendString(string str)
    {
        _webSocketConnection.Send(str);
    }

    internal void HandleOpen()
    {
        try
        {
            var path = _webSocketConnection.ConnectionInfo.Path;
            var match = _options.UrlMapping.Match(path);
            var streamName = match.Groups["streamName"].Value;
            var controllerName = match.Groups["controller"].Value;
            var query = "";
            var idx = path.IndexOf('?');
            if (idx != -1)
            {
                query = path.Substring(idx);
            }
            if (!_options._controllers.TryGetValue(controllerName.ToLower(), out var controllerType))
            {
                _webSocketConnection.Close();
            }
            _controller = _options._serverOptions.ServerLifetime.Resolve(controllerType) as WebSocketController;
            _controller.Query = HttpUtility.ParseQueryString(query);
            _controller.StreamName = streamName;
            _controller.Session = this;
            _controller.OnConnect().ContinueWith(_ =>
            {
                _webSocketConnection.Close();
            }, TaskContinuationOptions.OnlyOnFaulted); ;
        }
        catch
        {
            _webSocketConnection.Close();
        }
    }

    public Task SendFlvHeaderAsync(bool hasAudio, bool hasVideo)
    {
        return SendRawDataAsync(_flvMuxer.MultiplexFlvHeader(hasAudio, hasVideo));
    }

    public Task SendMessageAsync(Message data)
    {
        return SendRawDataAsync(_flvMuxer.MultiplexFlv(data));
    }

    internal void HandleClose()
    {
        if (_controller is IDisposable disp)
        {
            disp.Dispose();
        }
        _controller = null;
    }

    internal void HandleMessage(string msg)
    {
        _controller?.OnMessage(msg);
    }
}