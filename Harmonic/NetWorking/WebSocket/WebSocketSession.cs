using Autofac;
using Fleck;
using Harmonic.Buffers;
using Harmonic.Controllers;
using Harmonic.Hosting;
using Harmonic.NetWorking.Amf.Serialization.Amf0;
using Harmonic.NetWorking.Amf.Serialization.Amf3;
using Harmonic.NetWorking.Rtmp.Messages;
using Harmonic.NetWorking.Rtmp.Serialization;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Harmonic.NetWorking.Utils;
using Harmonic.NetWorking.Rtmp.Data;
using System.Linq;

namespace Harmonic.NetWorking.WebSocket
{
    public class WebSocketSession
    {
        private IWebSocketConnection _webSocketConnection = null;
        private WebSocketOptions _options = null;
        private WebSocketController _controller = null;
        private Amf0Writer _amf0Writer = new Amf0Writer();
        private Amf3Writer _amf3Writer = new Amf3Writer();

        public WebSocketSession(IWebSocketConnection connection, WebSocketOptions options)
        {
            _webSocketConnection = connection;
            _options = options;
        }

        public void SendRawData(byte[] data)
        {
            _webSocketConnection.Send(data);
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
                _controller.Query = query;
                _controller.StreamName = streamName;
                _controller.Session = this;
                _controller.OnConnect();
            }
            catch
            {
                _webSocketConnection.Close();
            }
        }

        public void SendFlvHeader(bool hasAudio, bool hasVideo)
        {
            var header = new byte[13];
            header[0] = 0x46;
            header[1] = 0x4C;
            header[2] = 0x56;
            header[3] = 0x01;

            byte audioFlag = 0x01 << 2;
            byte videoFlag = 0x01;
            byte typeFlag = 0x00;
            if (hasAudio) typeFlag |= audioFlag;
            if (hasVideo) typeFlag |= videoFlag;
            header[4] = typeFlag;

            NetworkBitConverter.TryGetBytes(9, header.AsSpan(5));
            SendRawData(header);
        }

        public void SendMessage(Message data)
        {
            var dataBuffer = new ByteBuffer();
            dataBuffer.WriteToBuffer((byte)data.MessageHeader.MessageType);
            var buffer = new byte[4];
            NetworkBitConverter.TryGetUInt24Bytes(data.MessageHeader.MessageLength, buffer);
            dataBuffer.WriteToBuffer(buffer.AsSpan(0, 3));
            NetworkBitConverter.TryGetBytes(data.MessageHeader.Timestamp, buffer);
            dataBuffer.WriteToBuffer(buffer.AsSpan(1, 3));
            dataBuffer.WriteToBuffer(buffer.AsSpan(0, 1));
            buffer.AsSpan().Clear();
            dataBuffer.WriteToBuffer(buffer.AsSpan(0, 3));

            var context = new Rtmp.Serialization.SerializationContext()
            {
                Amf0Writer = _amf0Writer,
                Amf3Writer = _amf3Writer,
                WriteBuffer = dataBuffer
            };

            data.Serialize(context);
            NetworkBitConverter.TryGetBytes((uint)(data.MessageHeader.MessageLength + 11), buffer);
            dataBuffer.WriteToBuffer(buffer);

            var rawData = new byte[dataBuffer.Length];
            dataBuffer.TakeOutMemory(rawData);
            SendRawData(rawData);
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
}
