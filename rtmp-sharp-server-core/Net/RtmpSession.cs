using Complete;
using Complete.Threading;
using RtmpSharp.Controller;
using RtmpSharp.IO;
using RtmpSharp.Messaging;
using RtmpSharp.Messaging.Events;
using RtmpSharp.Messaging.Messages;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RtmpSharp.Net
{
    class RtmpSession : IDisposable, IStreamSession
    {
        public VideoData CurrentVideoData { get; private set; }
        public AudioData CurrentAudioData { get; private set; }
        public event EventHandler Disconnected;
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;
        public event EventHandler<Exception> CallbackException;
        int invokeId = 0;
        public string ConnectedApp { get; private set; }
        public bool HasConnected { get; private set; } = false;
        private DateTime connectTime;
        public bool IsDisconnected => disconnectsFired != 0;
        volatile int disconnectsFired = 0;
        readonly TaskCallbackMachine<int, object> callbackManager;
        readonly TaskCallbackMachine<int, object> pingManager;
        public RtmpPacketWriter writer = null;
        public RtmpPacketReader reader = null;
        ObjectEncoding objectEncoding;
        Socket clientSocket;
        public ushort StreamId { get; private set; } = 0;
        public ushort ClientId { get; private set; } = 0;
        private string _app;
        public NotifyAmf0 FlvMetaData { get; private set; }
        public bool IsPublishing { get; private set; } = false;
        public bool IsPlaying { get; private set; } = false;
        public bool AudioSended { get; internal set; }

        private const int CONTROL_CSID = 2;
        private Random random = new Random();
        private AbstractController _controller = null;
        private Type _controllerType = null;
        public dynamic SessionStorage { get; set; } = null;
        public RtmpServer Server { get; private set; } = null;

        public RtmpSession(Socket client_socket, Stream stream, RtmpServer server, ushort client_id, SerializationContext context, ObjectEncoding objectEncoding = ObjectEncoding.Amf0, bool asyncMode = false)
        {
            ClientId = client_id;
            clientSocket = client_socket;
            Server = server;
            this.objectEncoding = objectEncoding;
            writer = new RtmpPacketWriter(new AmfWriter(stream, context, ObjectEncoding.Amf0, asyncMode), ObjectEncoding.Amf0);
            reader = new RtmpPacketReader(new AmfReader(stream, context, asyncMode));
            reader.EventReceived += EventReceivedCallback;
            reader.Disconnected += OnPacketProcessorDisconnected;
            writer.Disconnected += OnPacketProcessorDisconnected;
            callbackManager = new TaskCallbackMachine<int, object>();
            pingManager = new TaskCallbackMachine<int, object>();
        }

        public event ChannelDataReceivedEventHandler ChannelDataReceived;

        public void Close()
        {
            Disconnect(new ExceptionalEventArgs("disconnected"));
        }

        void OnPacketProcessorDisconnected(object sender, ExceptionalEventArgs args)
        {
            Disconnect(args);
        }

        public void WriteOnce()
        {
            writer.WriteOnce();
        }

        public void ReadOnce()
        {
            reader.ReadOnce();
        }

        public Task WriteOnceAsync(CancellationToken ct = default)
        {
            return writer.WriteOnceAsync(ct);
        }

        public Task StartReadAsync(CancellationToken ct = default)
        {
            var tsk = reader.ReadOnceAsync(ct);
            tsk.ContinueWith(t =>
            {
                StartReadAsync(ct);
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
            tsk.ContinueWith((t, o) =>
            {
                Close();
            }, TaskContinuationOptions.OnlyOnFaulted);
            return tsk;
        }

        Task<object> QueueCommandAsTask(Command command, int streamId, int messageStreamId, bool requireConnected = true)
        {
            if (requireConnected && IsDisconnected)
                return CreateExceptedTask(new ClientDisconnectedException("disconnected"));

            var task = callbackManager.Create(command.InvokeId);
            writer.Queue(command, streamId, random.Next());
            return task;
        }

        public void Disconnect(ExceptionalEventArgs e)
        {
            if (Interlocked.Increment(ref disconnectsFired) > 1)
                return;

            HasConnected = false;
            WrapCallback(() => Disconnected?.Invoke(this, e));
            WrapCallback(() => callbackManager.SetExceptionForAll(new ClientDisconnectedException(e.Description, e.Exception)));
            Dispose(true);
        }

        void EventReceivedCallback(object sender, EventReceivedEventArgs e)
        {
            MessageReceived?.Invoke(this, new MessageReceivedEventArgs(ClientId.ToString(), e.Event.MessageType.ToString(), e.Event));
            switch (e.Event.MessageType)
            {
                case MessageType.UserControlMessage:
                    var m = (UserControlMessage)e.Event;
                    if (m.EventType == UserControlMessageType.PingRequest)
                    {
                        Console.WriteLine("Client Ping Request");
                        WriteProtocolControlMessage(new UserControlMessage(UserControlMessageType.PingResponse, m.Values));
                    }
                    else if (m.EventType == UserControlMessageType.SetBufferLength)
                    {
                        Console.WriteLine("Set Buffer Length");
                    }
                    else if (m.EventType == UserControlMessageType.PingResponse)
                    {
                        Console.WriteLine("Ping Response");
                        var message = m as UserControlMessage;
                        pingManager.SetResult(message.Values[0], null);
                    }
                    break;
                case MessageType.DataAmf3:
                    break;
                case MessageType.DataAmf0:
                    {
                        var command = (Command)e.Event;
                        if (_controller == null || _controllerType == null)
                        {
                            throw new EntryPointNotFoundException();
                        }
                        var method = _controllerType.GetMethod(command.MethodCall.Name);
                        if (method == null)
                        {
                            throw new EntryPointNotFoundException();
                        }
                        var ret = method.Invoke(_controller, new object[] { command });
                        if (ret is Task tsk)
                        {
                            tsk.ContinueWith(t =>
                            {
                                ReturnResultInvoke(null, command.InvokeId, t.Exception.Message, true, false);
                            }, TaskContinuationOptions.OnlyOnFaulted);
                        }
                    }
                    break;
                case MessageType.CommandAmf3:
                case MessageType.CommandAmf0:
                    {
                        var command = (Command)e.Event;
                        var call = command.MethodCall;
                        var param = call.Parameters.Length == 1 ? call.Parameters[0] : call.Parameters;
                        switch (call.Name)
                        {
                            case "connect":
                                StreamId = Server.RequestStreamId();
                                HandleConnectInvoke(command);
                                HasConnected = true;
                                Server.RegisteredApps.TryGetValue(_app, out _controllerType);
                                if (!_controllerType.IsAbstract)
                                {
                                    _controller = Activator.CreateInstance(_controllerType) as AbstractController;
                                }
                                else
                                {
                                    throw new InvalidOperationException();
                                }
                                break;
                            case "_result":
                                // unwrap Flex class, if present
                                var ack = param as AcknowledgeMessage;
                                callbackManager.SetResult(command.InvokeId, ack != null ? ack.Body : param);
                                break;

                            case "_error":
                                // unwrap Flex class, if present
                                var error = param as ErrorMessage;
                                callbackManager.SetException(command.InvokeId, error != null ? new InvocationException(error) : new InvocationException());
                                break;
                            default:
                                if (_controller == null || _controllerType == null)
                                {
                                    throw new EntryPointNotFoundException();
                                }
                                var methodName = call.Name;
                                var method = _controllerType.GetMethod(methodName);
                                if (method != null)
                                {
                                    _controller?.EnsureSessionStorage();
                                    var ret = method.Invoke(_controller, command.MethodCall.Parameters);
                                    if (ret is Task tsk)
                                    {
                                        tsk.ContinueWith((t, obj) =>
                                        {
                                            Console.WriteLine($"Exception: {t.Exception.GetType().ToString()}, CallStack: {t.Exception.StackTrace}");
                                            ReturnResultInvoke(null, command.InvokeId, $"{t.Exception.GetType().ToString()}\t{t.Exception.Message}", true, false);
                                        }, TaskContinuationOptions.OnlyOnFaulted);
                                        tsk.ContinueWith((t, obj) =>
                                        {
                                            SetResultValInvoke(obj, command.InvokeId);
                                        }, TaskContinuationOptions.OnlyOnRanToCompletion);
                                    }
                                }
#if DEBUG
                                else
                                {
                                    System.Diagnostics.Debug.Print($"unknown rtmp command: {call.Name}");
                                    System.Diagnostics.Debugger.Break();
                                }
#endif
                                break;
                        }
                    }
                    break;
                case MessageType.Video:
                    _controller?.EnsureSessionStorage();
                    _controller?.OnVideo(e.Event as VideoData);
                    break;
                case MessageType.Audio:
                    _controller?.EnsureSessionStorage();
                    _controller?.OnAudio(e.Event as AudioData);
                    break;
                case MessageType.WindowAcknowledgementSize:
                    var msg = (WindowAcknowledgementSize)e.Event;
                    break;
                case MessageType.Acknowledgement:
                    break;
                default:
                    Console.WriteLine(string.Format("Unknown message type {0}", e.Event.MessageType));
                    break;
            }
        }
        public Task<T> InvokeAsync<T>(string method, object argument)
        {
            return InvokeAsync<T>(method, new[] { argument });
        }

        public async Task<T> InvokeAsync<T>(string method, object[] arguments)
        {
            var result = await QueueCommandAsTask(new InvokeAmf0
            {
                MethodCall = new Method(method, arguments),
                InvokeId = GetNextInvokeId()
            }, 3, 0);
            return (T)MiniTypeConverter.ConvertTo(result, typeof(T));
        }

        public void SendAmf0Data(RtmpEvent e)
        {
            //var timestamp = (int)(DateTime.UtcNow - connectTime).TotalMilliseconds;
            //e.Timestamp = timestamp;
            writer.Queue(e, StreamId, random.Next());
        }


        public Task<T> InvokeAsync<T>(string endpoint, string destination, string method, object argument)
        {
            return InvokeAsync<T>(endpoint, destination, method, new[] { argument });
        }

        public async Task<T> InvokeAsync<T>(string endpoint, string destination, string method, object[] arguments)
        {
            if (objectEncoding != ObjectEncoding.Amf3)
                throw new NotSupportedException("Flex RPC requires AMF3 encoding.");
            var client_id = Guid.NewGuid().ToString("D");
            var remotingMessage = new RemotingMessage
            {
                ClientId = client_id,
                Destination = destination,
                Operation = method,
                Body = arguments,
                Headers = new Dictionary<string, object>()
                {
                    { FlexMessageHeaders.Endpoint, endpoint },
                    { FlexMessageHeaders.FlexClientId, client_id ?? "nil" }
                }
            };

            var result = await QueueCommandAsTask(new InvokeAmf3()
            {
                InvokeId = GetNextInvokeId(),
                MethodCall = new Method(null, new object[] { remotingMessage })
            }, 3, 0);
            return (T)MiniTypeConverter.ConvertTo(result, typeof(T));
        }

        void @setDataFrame(Command command)
        {
            if ((string)command.ConnectionParameters != "onMetaData")
            {
                Console.WriteLine("Can only set metadata");
                throw new InvalidOperationException("Can only set metadata");
            }
            FlvMetaData = (NotifyAmf0)command;
        }

        async Task HandlePublishAsync(Command command)
        {
            string path = (string)command.MethodCall.Parameters[0];
            if (!await Server.RegisterPublish(_app, path, ClientId))
            {
                Disconnect(new ExceptionalEventArgs("Server publish error"));
                return;
            }
            var status = new AsObject
            {
                {"level", "status" },
                {"code", "NetStream.Publish.Start" },
                {"description", "Stream is now published." },
                {"details", path }
            };

            var call_on_status = new InvokeAmf0
            {
                MethodCall = new Method("onStatus", new object[] { status }),
                InvokeId = 0,
                ConnectionParameters = null,
            };
            call_on_status.MethodCall.CallStatus = CallStatus.Request;
            call_on_status.MethodCall.IsSuccess = true;

            // result.MessageType = MessageType.UserControlMessage;
            var stream_begin = new UserControlMessage(UserControlMessageType.StreamBegin, new int[] { StreamId });
            WriteProtocolControlMessage(stream_begin);
            writer.Queue(call_on_status, StreamId, random.Next());
            SetResultValInvoke(new object(), command.InvokeId);
            IsPublishing = true;
        }

        public void NotifyStatus(AsObject status)
        {
            var onStatusCommand = new InvokeAmf0
            {
                MethodCall = new Method("onStatus", new object[] { status }),
                InvokeId = 0,
                ConnectionParameters = null,
            };
            onStatusCommand.MethodCall.CallStatus = CallStatus.Request;
            onStatusCommand.MethodCall.IsSuccess = true;
            writer.Queue(onStatusCommand, StreamId, random.Next());
        }

        void SetResultValInvoke(object param, int transcationId)
        {
            ReturnResultInvoke(null, transcationId, param);
        }

        void ReturnResultInvoke(object connectParameters, int transcationId, object param, bool requiredConnected = true, bool success = true)
        {
            var result = new InvokeAmf0
            {
                MethodCall = new Method("_result", new object[] { param }),
                InvokeId = transcationId,
                ConnectionParameters = connectParameters
            };
            result.MethodCall.CallStatus = CallStatus.Result;
            result.MethodCall.IsSuccess = success;
            writer.Queue(result, StreamId, random.Next());
        }

        void HandleUnpublish(Command command)
        {
            IsPublishing = false;
            Server.UnRegisterPublish(ClientId);
        }

        void HandleConnectInvoke(Command command)
        {
            string code;
            string description;
            bool connect_result = false;
            var app = ((AsObject)command.ConnectionParameters)["app"];
            if (app == null)
            {
                Disconnect(new ExceptionalEventArgs("app value cannot be null"));
                return;
            }
            _app = app.ToString();
            if (!Server.AuthApp(app.ToString(), ClientId))
            {
                code = "NetConnection.Connect.Error";
                description = "Connection Failure.";
            }
            else
            {
                code = "NetConnection.Connect.Success";
                description = "Connection succeeded.";
                connect_result = true;
            }
            connectTime = DateTime.UtcNow;
            AsObject param = new AsObject
            {
                { "code", code },
                { "description", description },
                { "level", "status" },
            };
            ReturnResultInvoke(new AsObject {
                    { "capabilities", 255.00 },
                    { "fmsVer", "FMS/4,5,1,484" },
                    { "mode", 1.0 }
                }, command.InvokeId, param, false, connect_result);
            if (!connect_result)
            {
                Disconnect(new ExceptionalEventArgs("Auth Failure"));
                return;
            }
        }

        public Task PingAsync(int PingTimeout)
        {
            Console.WriteLine("Server Ping Request");
            var timestamp = (int)((DateTime.UtcNow - connectTime).TotalSeconds);
            var ping = new UserControlMessage(UserControlMessageType.PingRequest, new int[] { timestamp });
            WriteProtocolControlMessage(ping);
            var ret = pingManager.Create(timestamp);
            return ret;
        }

        public void SendRawData(byte[] data)
        {
            writer.writer.WriteBytes(data);
        }

        public void WriteProtocolControlMessage(RtmpEvent @event)
        {
            writer.Queue(@event, CONTROL_CSID, 0);
        }

        int GetNextInvokeId()
        {
            // interlocked.increment wraps overflows
            return Interlocked.Increment(ref invokeId);
        }

        void WrapCallback(Action action)
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

        static Task<object> CreateExceptedTask(Exception exception)
        {
            var source = new TaskCompletionSource<object>();
            source.SetException(exception);
            return source.Task;
        }

        #region IDisposable Support
        private bool disposedValue = false; // 要检测冗余调用

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    clientSocket.Close();
                    reader.reader.Dispose();
                }

                disposedValue = true;
            }
        }

        // 添加此代码以正确实现可处置模式。
        void IDisposable.Dispose()
        {
            Dispose(true);
        }


        #endregion
    }
}
