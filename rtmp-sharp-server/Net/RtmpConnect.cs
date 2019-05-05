using Complete;
using Complete.Threading;
using RtmpSharp.IO;
using RtmpSharp.Messaging;
using RtmpSharp.Messaging.Events;
using RtmpSharp.Messaging.Messages;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RtmpSharp.Net
{
    class RtmpConnect : IDisposable, IStreamConnect
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
        readonly TaskCallbackManager<int, object> callbackManager;
        public RtmpPacketWriter writer;
        public RtmpPacketReader reader;
        ObjectEncoding objectEncoding;
        RtmpServer server;
        Socket clientSocket;
        public ushort StreamId { get; private set; } = 0;
        public ushort ClientId { get; private set; } = 0;
        private string app;
        public NotifyAmf0 FlvMetaData { get; private set; }
        public bool IsPublishing { get; private set; } = false;
        public bool IsPlaying { get; private set; } = false;
        public bool AudioSended { get; internal set; }
        public VideoData AvCConfigureRecord { get; private set; } = null;
        public AudioData AACConfigureRecord { get; private set; } = null;
        private bool is_not_set_video_config = true;
        private bool is_not_set_auido_config = true;
        private const int CONTROL_CSID = 2;
        private Random random = new Random();

        public RtmpConnect(Socket client_socket, Stream stream, RtmpServer server, ushort client_id, SerializationContext context, ObjectEncoding objectEncoding = ObjectEncoding.Amf0, bool asyncMode = false)
        {
            ClientId = client_id;
            clientSocket = client_socket;
            this.server = server;
            this.objectEncoding = objectEncoding;
            writer = new RtmpPacketWriter(new AmfWriter(stream, context, ObjectEncoding.Amf0, asyncMode), ObjectEncoding.Amf0);
            reader = new RtmpPacketReader(new AmfReader(stream, context, asyncMode));
            reader.EventReceived += EventReceivedCallback;
            reader.Disconnected += OnPacketProcessorDisconnected;
            writer.Disconnected += OnPacketProcessorDisconnected;
            callbackManager = new TaskCallbackManager<int, object>();
            
        }
        
        public event ChannelDataReceivedEventHandler ChannelDataReceived;
        
        public void Close()
        {
            OnDisconnected(new ExceptionalEventArgs("disconnected"));
        }

        void OnPacketProcessorDisconnected(object sender, ExceptionalEventArgs args)
        {
            OnDisconnected(args);
        }

        public bool WriteOnce()
        {
            return writer.WriteOnce();
        }

        public bool ReadOnce()
        {
            return reader.ReadOnce();
        }

        public Task WriteOnceAsync()
        {
            return writer.WriteOnceAsync();
        }

        public Task ReadOnceAsync()
        {
            return reader.ReadOnceAsync();
        }


        Task<object> QueueCommandAsTask(Command command, int streamId, int messageStreamId, bool requireConnected = true)
        {
            if (requireConnected && IsDisconnected)
                return CreateExceptedTask(new ClientDisconnectedException("disconnected"));

            var task = callbackManager.Create(command.InvokeId);
            writer.Queue(command, streamId, random.Next());
            return task;
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

        void EventReceivedCallback(object sender, EventReceivedEventArgs e)
        {
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
                        // TODO
                    }
                    else if (m.EventType == UserControlMessageType.PingResponse)
                    {
                        Console.WriteLine("Ping Response");
                        var message = m as UserControlMessage;
                        callbackManager.SetResult(message.Values[0], null);
                    }
                    break;

                case MessageType.DataAmf3:
#if DEBUG
                    System.Diagnostics.Debugger.Break();
#endif
                    break;

                case MessageType.CommandAmf3:
                case MessageType.DataAmf0:
                case MessageType.CommandAmf0:
                    var command = (Command)e.Event;
                    var call = command.MethodCall;
                    var param = call.Parameters.Length == 1 ? call.Parameters[0] : call.Parameters;
                    switch (call.Name)
                    {
                        case "connect":
                            Console.WriteLine("Connect");
                            StreamId = server.RequestStreamId();
                            HandleConnectInvokeAsync(command);
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
                        case "receiveAudio":
                            // TODO
                            break;
                        case "releaseStream":
                            Console.WriteLine("ReleaseStream");
                            // TODO
                            break;
                        case "publish":
                            Console.WriteLine("publish");
                            HandlePublish(command);
                            break;
                        case "unpublish":
                            HandleUnpublish(command);
                            break;
                        case "FCpublish":
                        case "FCPublish":
                            Console.WriteLine("FCPublish");
                            // TODO
                            break;
                        case "FCUnpublish":
                        case "FCunPublish":
                            Console.WriteLine("FCUnpublish");
                            HandleUnpublish(command);
                            break;
                        case "createStream":
                            SetResultValInvoke(StreamId, command.InvokeId);
                            Console.WriteLine("create stream");
                            // TODO
                            break;
                        case "play":
                            Console.WriteLine("play");
                            // TODO
                            HandlePlay(command);
                            break;
                        case "deleteStream":
                            Console.WriteLine("deleteStream");
                            // TODO
                            break;
                        case "@setDataFrame":
                            Console.WriteLine("SetDataFrame");
                            SetDataFrame(command);
                            // TODO
                            break;
                        default:
#if DEBUG
                            System.Diagnostics.Debug.Print($"unknown rtmp command: {call.Name}");
                            System.Diagnostics.Debugger.Break();
#endif
                            break;
                    }
                    break;
                case MessageType.WindowAcknowledgementSize:
                    var msg = (WindowAcknowledgementSize)e.Event;
                    break;
                case MessageType.Video:
                    var video_data = e.Event as VideoData;
                    if (is_not_set_video_config && video_data.Data.Length >= 2 && video_data.Data[1] == 0)
                    {
                        is_not_set_video_config = false;
                        AvCConfigureRecord = video_data;
                    }
                    if (ChannelDataReceived != null)
                    {
                        ChannelDataReceived(this, new ChannelDataReceivedEventArgs(ChannelType.Video, e.Event));
                    }
                    break;
                case MessageType.Audio:
                    var audio_data = e.Event as AudioData;
                    if (is_not_set_auido_config && audio_data.Data.Length >= 2 && audio_data.Data[1] == 0)
                    {
                        is_not_set_auido_config = false;
                        AACConfigureRecord = audio_data;
                    }
                    if (ChannelDataReceived != null)
                    {
                        ChannelDataReceived(this, new ChannelDataReceivedEventArgs(ChannelType.Audio, e.Event));
                    }
                    break;
                case MessageType.Acknowledgement:
                    break;
                default:
                    Console.WriteLine(string.Format("Unknown message type {0}", e.Event.MessageType));
                    break;
            }
        }

        private void HandlePlay(Command command)
        {
            string path = (string)command.MethodCall.Parameters[0];
            if (!server.RegisterPlay(app, path, ClientId))
            {
                OnDisconnected(new ExceptionalEventArgs("play parameter auth failed"));
                return;
            }
            WriteProtocolControlMessage(new UserControlMessage(UserControlMessageType.StreamIsRecorded, new int[] { StreamId }));
            WriteProtocolControlMessage(new UserControlMessage(UserControlMessageType.StreamBegin, new int[] { StreamId }));
            
            var status_reset = new AsObject
            {
                {"level", "status" },
                {"code", "NetStream.Play.Reset" },
                {"description", "Resetting and playing stream." },
                {"details", path }
            };

            var call_on_status_reset = new InvokeAmf0
            {
                MethodCall = new Method("onStatus", new object[] { status_reset }),
                InvokeId = 0,
                ConnectionParameters = null,
            };
            call_on_status_reset.MethodCall.CallStatus = CallStatus.Request;
            call_on_status_reset.MethodCall.IsSuccess = true;

            var status_start = new AsObject
            {
                {"level", "status" },
                {"code", "NetStream.Play.Start" },
                {"description", "Started playing." },
                {"details", path }
            };

            var call_on_status_start = new InvokeAmf0
            {
                MethodCall = new Method("onStatus", new object[] { status_start }),
                InvokeId = 0,
                ConnectionParameters = null,
            };
            call_on_status_start.MethodCall.CallStatus = CallStatus.Request;
            call_on_status_start.MethodCall.IsSuccess = true;
            writer.Queue(call_on_status_reset, StreamId, random.Next());
            writer.Queue(call_on_status_start, StreamId, random.Next());
            try
            {
                server.SendMetadata(app, path, this);
                server.ConnectToClient(app, path, ClientId, ChannelType.Video);
                server.ConnectToClient(app, path, ClientId, ChannelType.Audio);
            }
            catch (Exception e)
            { OnDisconnected(new ExceptionalEventArgs("Not Found", e)); }
            IsPlaying = true;
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

        void SetDataFrame(Command command)
        {
            if ((string)command.ConnectionParameters != "onMetaData")
            {
                Console.WriteLine("Can only set metadata");
                throw new InvalidOperationException("Can only set metadata");
            }
            FlvMetaData = (NotifyAmf0)command;
        }

        void HandlePublish(Command command)
        {
            string path = (string)command.MethodCall.Parameters[0];
            if (!server.RegisterPublish(app, path, ClientId))
            {
                OnDisconnected(new ExceptionalEventArgs("Server publish error"));
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
            Console.WriteLine("_result");
        }

        void HandleUnpublish(Command command)
        {
            IsPublishing = false;
            server.UnRegisterPublish(ClientId);
        }

        void HandleConnectInvokeAsync(Command command)
        {
            string code;
            string description;
            bool connect_result = false;
            var app = ((AsObject)command.ConnectionParameters)["app"];
            if (app == null)
            {
                OnDisconnected(new ExceptionalEventArgs("app value cannot be null"));
                return;
            }
            this.app = app.ToString();
            if (!server.AuthApp(app.ToString(), ClientId))
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
                OnDisconnected(new ExceptionalEventArgs("Auth Failure"));
                return;
            }
        }

        public Task PingAsync(int PingTimeout)
        {
            //Console.WriteLine("Server Ping Request");
            //var timestamp = (int)((DateTime.UtcNow -  connectTime).TotalSeconds);
            //var ping = new UserControlMessage(UserControlMessageType.PingRequest, new int[] { timestamp });
            //WriteProtocolControlMessage(ping);
            //var ret = callbackManager.Create(timestamp);

            //return ret;
            return null;
        }

        public void SendRawData(byte[] data)
        {
            writer.writer.WriteBytes(data);
        }
        
        void WriteProtocolControlMessage(RtmpEvent @event)
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
