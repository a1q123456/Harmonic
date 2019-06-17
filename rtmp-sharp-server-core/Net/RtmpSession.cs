using Autofac;
using Complete;
using Complete.Threading;
using RtmpSharp.Controller;
using RtmpSharp.Hosting;
using RtmpSharp.IO;
using RtmpSharp.Messaging;
using RtmpSharp.Messaging.Events;
using RtmpSharp.Messaging.Messages;
using RtmpSharp.Rpc;
using System;
using System.Reflection;
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
        public ushort ClientId { get; private set; } = 0;
        private Random random = new Random();
        private AbstractController _controller = null;
        private Type _controllerType = null;
        public dynamic SessionStorage { get; set; } = null;
        public RtmpServer Server { get; private set; } = null;
        private ILifetimeScope SessionLifetime { get; set; } = null;
        public ConnectionInformation ConnectionInformation { get; private set; } = null;
        public int BufferMilliseconds { get; set; } = 0;
        private const int CONTROL_CSID = 2;

        public RtmpSession(Socket client_socket, Stream stream, RtmpServer server, ushort client_id, SerializationContext context, ObjectEncoding objectEncoding = ObjectEncoding.Amf0, bool asyncMode = false)
        {
            ClientId = client_id;
            clientSocket = client_socket;
            Server = server;
            this.objectEncoding = objectEncoding;
            writer = new RtmpPacketWriter(stream, context, ObjectEncoding.Amf0);
            reader = new RtmpPacketReader(new AmfReader(stream, context, asyncMode));
            reader.EventReceived += EventReceivedCallback;
            reader.Aborted += (s, e) =>
            {
                WriteProtocolControlMessage(new Abort(e));
            };
            reader.Disconnected += OnPacketProcessorDisconnected;
            writer.Disconnected += OnPacketProcessorDisconnected;
            callbackManager = new TaskCallbackMachine<int, object>();
            pingManager = new TaskCallbackMachine<int, object>();
            StartReadAsync();
            StartWriteAsync();
            SessionLifetime = Server.ServiceContainer.BeginLifetimeScope();
        }
        public void Close()
        {
            Disconnect(new ExceptionalEventArgs("disconnected"));
        }

        void OnPacketProcessorDisconnected(object sender, ExceptionalEventArgs args)
        {
            Disconnect(args);
        }

        public Task WriteOnceAsync(CancellationToken ct = default)
        {
            if (disposedValue)
            {
                throw new ObjectDisposedException("session already disposed");
            }
            return writer.WriteOnceAsync(ct);
        }
        public Task StartWriteAsync(CancellationToken ct = default)
        {
            if (disposedValue)
            {
                throw new ObjectDisposedException("session already disposed");
            }
            var tsk = writer.WriteOnceAsync(ct);
            tsk.ContinueWith(t =>
            {
                StartWriteAsync(ct);
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
            tsk.ContinueWith(t =>
            {
                foreach (var excep in t.Exception.InnerExceptions)
                {
                    Console.WriteLine(excep.ToString());
                }

                Close();
            }, TaskContinuationOptions.NotOnRanToCompletion);
            return tsk;
        }
        public Task StartReadAsync(CancellationToken ct = default)
        {
            if (disposedValue)
            {
                throw new ObjectDisposedException("session already disposed");
            }
            var tsk = reader.ReadOnceAsync(ct);
            tsk.ContinueWith(t =>
            {
                StartReadAsync(ct);
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
            tsk.ContinueWith(t =>
            {
                foreach (var excep in t.Exception.InnerExceptions)
                {
                    Console.WriteLine(excep.ToString());
                }

                Close();
            }, TaskContinuationOptions.NotOnRanToCompletion);
            return tsk;
        }

        async Task<object> CallCommandAsync(Command command, int messageStreamId, int chunkStreamId, bool requireConnected = true, CancellationToken ct = default)
        {
            if (disposedValue)
            {
                throw new ObjectDisposedException("session already disposed");
            }
            if (requireConnected && IsDisconnected)
                return CreateExceptedTask(new ClientDisconnectedException("disconnected"));

            var task = callbackManager.Create(command.InvokeId, ct);
            writer.WriteMessage(command, messageStreamId, chunkStreamId);
            return await task;
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
                        BufferMilliseconds = m.Values[1];
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
                        var targetMethod = FindMethod(command).FirstOrDefault();

                        if (targetMethod == default)
                        {
                            throw new EntryPointNotFoundException();
                        }
                        var ret = targetMethod.Invoke(_controller, new object[] { command });
                        var chunkStreamId = GetMethodChunkStreamId(targetMethod);
                        if (ret is Task tsk)
                        {
                            tsk.ContinueWith(t =>
                            {
                                ReturnResultInvoke(null, command.InvokeId, t.Exception.Message, command.MessageStreamId, chunkStreamId, true, false);
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
                                HandleConnectInvoke(command);
                                HasConnected = true;
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
                                var method = ChooseMethodByParameter(command, FindMethod(command));
                                Console.WriteLine($"Invoke method {methodName}");
                                if (method != null)
                                {
                                    _controller?.EnsureSessionStorage();
                                    var parameters = command.MethodCall.Parameters.ToList();
                                    if (parameters.Count < method.GetParameters().Length)
                                    {
                                        var pad = new object[method.GetParameters().Length - parameters.Count];
                                        Array.Fill(pad, Type.Missing);
                                        parameters.AddRange(pad);
                                    }
                                    var ret = method.Invoke(_controller, parameters.ToArray());
                                    if (command.InvokeId != 0)
                                    {
                                        var chunkStreamId = GetMethodChunkStreamId(method);
                                        if (ret is Task tsk)
                                        {
                                            tsk.ContinueWith(t =>
                                            {
                                                if (t.Exception is AggregateException agg)
                                                {
                                                    foreach (var excep in t.Exception.InnerExceptions)
                                                    {
                                                        Console.WriteLine(excep.ToString());
                                                    }
                                                }
                                                else
                                                {
                                                    Console.WriteLine(t.Exception.ToString());
                                                }

                                                ReturnResultInvoke(null, command.InvokeId, $"{t.Exception.GetType().ToString()}\t{t.Exception.Message}", command.MessageStreamId, chunkStreamId, true, false);
                                            }, TaskContinuationOptions.OnlyOnFaulted);

                                            tsk.ContinueWith(t =>
                                            {
                                                if (t.GetType().IsGenericType && t.GetType().GetGenericTypeDefinition() == typeof(Task<>))
                                                {
                                                    var result = t.GetType().GetProperty("Result").GetValue(t);
                                                    if (command.MethodCall.Name == "createStream" || command.MethodCall.Name == "deleteStream")
                                                    {
                                                        writer.SingleMessageStreamId = _controller.CreatedStreams.Count <= 1;
                                                    }
                                                    SetResultValInvoke(result, command.InvokeId, command.MessageStreamId, chunkStreamId);
                                                }
                                            }, TaskContinuationOptions.OnlyOnRanToCompletion);

                                        }
                                        else if (method.ReturnType != typeof(void))
                                        {
                                            SetResultValInvoke(ret, command.InvokeId, command.MessageStreamId, chunkStreamId);
                                        }
                                    }

                                }
#if DEBUG
                                else
                                {
                                    Console.WriteLine($"unknown rtmp command: {call.Name}");
                                    //System.Diagnostics.Debugger.Break();
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
        public Task<T> InvokeAsync<T>(string method, object argument, int messageStreamId)
        {
            if (disposedValue)
            {
                throw new ObjectDisposedException("session already disposed");
            }
            return InvokeAsync<T>(method, new[] { argument }, messageStreamId);
        }

        public async Task<T> InvokeAsync<T>(string method, object[] arguments, int messageStreamId, int chunkStreamId)
        {
            if (disposedValue)
            {
                throw new ObjectDisposedException("session already disposed");
            }
            var result = await CallCommandAsync(new InvokeAmf0
            {
                MethodCall = new Method(method, arguments),
                InvokeId = GetNextInvokeId()
            }, messageStreamId, chunkStreamId);
            return (T)MiniTypeConverter.ConvertTo(result, typeof(T));
        }

        public void SendAmf0Data(RtmpEvent e, int messageStreamId, int chunkStreamId)
        {
            if (disposedValue)
            {
                throw new ObjectDisposedException("session already disposed");
            }

            //var timestamp = (int)(DateTime.UtcNow - connectTime).TotalMilliseconds;
            //e.Timestamp = timestamp;
            writer.WriteMessage(e, messageStreamId, chunkStreamId);
        }

        public Task<T> InvokeAsync<T>(string endpoint, string destination, string method, object argument)
        {
            return InvokeAsync<T>(endpoint, destination, method, new[] { argument });
        }

        public async Task<T> InvokeAsync<T>(string endpoint, string destination, string method, object[] arguments, int messageStreamId, int chunkStreamId)
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

            var result = await CallCommandAsync(new InvokeAmf3()
            {
                InvokeId = GetNextInvokeId(),
                MethodCall = new Method(null, new object[] { remotingMessage })
            }, messageStreamId, chunkStreamId);
            return (T)MiniTypeConverter.ConvertTo(result, typeof(T));
        }
        public void NotifyStatus(AsObject status, int messageStreamId, int chunkStreamId)
        {
            var onStatusCommand = new InvokeAmf0
            {
                MethodCall = new Method("onStatus", new object[] { status }),
                InvokeId = 0,
                CommandObject = null,
            };
            onStatusCommand.MethodCall.CallStatus = CallStatus.Request;
            onStatusCommand.MethodCall.IsSuccess = true;
            writer.WriteMessage(onStatusCommand, messageStreamId, chunkStreamId);
        }

        void SetResultValInvoke(object param, int transcationId, int messageStreamId, int chunkStreamId)
        {
            ReturnResultInvoke(null, transcationId, param, messageStreamId, chunkStreamId);
        }

        void ReturnResultInvoke(object commandObject, int transcationId, object param, int messageStreamId, int chunkStreamId, bool requiredConnected = true, bool success = true)
        {
            var result = new InvokeAmf0
            {
                MethodCall = new Method("_result", new object[] { param }),
                InvokeId = transcationId,
                CommandObject = commandObject
            };
            result.MethodCall.CallStatus = CallStatus.Result;
            result.MethodCall.IsSuccess = success;
            writer.WriteMessage(result, messageStreamId, chunkStreamId);
        }

        void ReturnVoidInvoke(object commandObject, int transcationId, int messageStreamId, int chunkStreamId, bool requiredConnected = true)
        {
            var result = new InvokeAmf0
            {
                MethodCall = new Method("_result", new object[] { }),
                InvokeId = transcationId,
                CommandObject = commandObject
            };
            result.MethodCall.CallStatus = CallStatus.Result;
            result.MethodCall.IsSuccess = true;
            writer.WriteMessage(result, messageStreamId, chunkStreamId);
        }

        void SetupController()
        {
            if (!Server.RegisteredApps.TryGetValue(ConnectionInformation.App, out _controllerType))
            {
                Console.WriteLine("app not found");
                throw new EntryPointNotFoundException($"request app {ConnectionInformation.App} not found");
            }

            if (!_controllerType.IsAbstract)
            {
                var ctors = _controllerType.GetConstructors();
                if (ctors.Length != 1)
                {
                    throw new InvalidOperationException();
                }
                var parametersInfo = ctors.First().GetParameters();
                var parameters = new List<object>();
                foreach (var parameterInfo in parametersInfo)
                {
                    ILifetimeScope scope = null;
                    if (Server.SessionScopedServices.Contains(parameterInfo.ParameterType))
                    {
                        scope = SessionLifetime;
                    }
                    else
                    {
                        scope = Server.ServerLifetime;
                    }
                    parameters.Add(scope.Resolve(parameterInfo.ParameterType));
                }
                _controller = Activator.CreateInstance(_controllerType, parameters.ToArray()) as AbstractController;
                _controller.Session = this;
            }
            else
            {
                throw new InvalidOperationException();
            }
        }
        void HandleConnectInvoke(Command command)
        {
            string code;
            string description;
            bool connect_result = false;

            ConnectionInformation = new ConnectionInformation();

            var app = ((AsObject)command.CommandObject)["app"];
            if (app is string strApp)
            {
                ConnectionInformation.App = app as string;
            }
            else
            {
                Disconnect(new ExceptionalEventArgs("app value cannot be null"));
                return;
            }

            var props = ConnectionInformation.GetType().GetProperties();
            foreach (var prop in props)
            {
                var sb = new StringBuilder(prop.Name);
                sb[0] = char.ToLower(sb[0]);
                var asPropName = sb.ToString();
                if (command.CommandObject is AsObject commandObject)
                {
                    if (commandObject.ContainsKey(asPropName))
                    {
                        var commandObjectValue = commandObject[asPropName];
                        if (commandObjectValue.GetType() == prop.PropertyType)
                        {
                            prop.SetValue(ConnectionInformation, commandObject[asPropName]);
                        }
                    }
                }
            }

            if (!Server.AuthApp(ConnectionInformation.App))
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
            SetupController();
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

                }, command.InvokeId, param, 0, 3, false, connect_result);
            if (!connect_result)
            {
                Disconnect(new ExceptionalEventArgs("Auth Failure"));
                return;
            }
        }

        public async Task PingAsync(CancellationToken ct = default)
        {
            if (disposedValue)
            {
                throw new ObjectDisposedException("session already disposed");
            }
            Console.WriteLine("Server Ping Request");
            var timestamp = (int)((DateTime.UtcNow - connectTime).TotalSeconds);
            var ping = new UserControlMessage(UserControlMessageType.PingRequest, new int[] { timestamp });
            var ret = pingManager.Create(timestamp, ct);
            WriteProtocolControlMessage(ping);
            await ret;
            Console.WriteLine("Client Pong Response");
        }

        public void SendRawData(byte[] data)
        {
            if (disposedValue)
            {
                throw new ObjectDisposedException("session already disposed");
            }
            writer.QueueChunk(data);
        }

        public void WriteProtocolControlMessage(RtmpEvent @event)
        {
            if (disposedValue)
            {
                throw new ObjectDisposedException("session already disposed");
            }
            writer.WriteMessage(@event, 0, CONTROL_CSID);
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

        int GetMethodChunkStreamId(MethodInfo method)
        {
            if (method.GetCustomAttribute(typeof(RpcMethodAttribute)) is RpcMethodAttribute rpcMethodAttr)
            {
                return rpcMethodAttr.ChannelId;
            }
            throw new InvalidOperationException("this is not an rpc method");
        }

        MethodInfo ChooseMethodByParameter(Command command, List<MethodInfo> methods)
        {
            foreach (var method in methods)
            {
                var typeList = command.MethodCall.Parameters.Select(p => p.GetType()).ToArray();
                var targetMethod = _controllerType.GetMethod(method.Name, typeList);
                if (targetMethod != null)
                {
                    if (targetMethod.GetCustomAttribute(typeof(RpcMethodAttribute)) is RpcMethodAttribute rpcMethodAttr)
                    {
                        if (rpcMethodAttr.Name == command.MethodCall.Name)
                        {
                            return targetMethod;
                        }
                    }
                }
                else
                {
                    var parameters = method.GetParameters();
                    var requiredParameters = parameters.Where(p => !p.IsOptional).ToArray();
                    if (requiredParameters.Length <= command.MethodCall.Parameters.Length)
                    {
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            if (i < requiredParameters.Length)
                            {
                                if (parameters[i].ParameterType != command.MethodCall.Parameters[i].GetType())
                                {
                                    break;
                                }
                            }
                            else
                            {
                                if (i < command.MethodCall.Parameters.Length)
                                {
                                    if (parameters[i].ParameterType.IsGenericType && parameters[i].ParameterType.GetGenericTypeDefinition() == typeof(Nullable<>))
                                    {
                                        var underlying = Nullable.GetUnderlyingType(parameters[i].ParameterType);
                                        if (underlying != command.MethodCall.Parameters[i].GetType())
                                        {
                                            break;
                                        }
                                    }
                                    else
                                    {
                                        if (parameters[i].ParameterType != command.MethodCall.Parameters[i].GetType())
                                        {
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    return method;
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        List<MethodInfo> FindMethod(Command command)
        {
            var ret = new List<MethodInfo>();
            var methods = _controllerType.GetMethods();
            foreach (var method in methods)
            {
                if (method.IsPublic && !method.ContainsGenericParameters)
                {
                    var attributes = method.GetCustomAttributes(true);
                    foreach (var attr in attributes)
                    {
                        if (attr is RpcMethodAttribute rpcMethodAttr)
                        {
                            if (rpcMethodAttr.Name == command.MethodCall.Name)
                            {
                                ret.Add(method);
                            }
                            else if (rpcMethodAttr.Name == null && method.Name == command.MethodCall.Name)
                            {
                                ret.Add(method);
                            }
                        }
                    }
                }
            }
            return ret;
        }

        #region IDisposable Support
        private bool disposedValue = false; // 要检测冗余调用

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (_controller is IDisposable dispController)
                    {
                        dispController?.Dispose();
                    }

                    SessionLifetime.Dispose();
                    clientSocket.Close();
                    reader.Dispose();
                    reader = null;
                    writer = null;
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
