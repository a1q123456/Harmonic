using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Complete;
using RtmpSharp.IO;
using RtmpSharp.Messaging;
using RtmpSharp.Messaging.Events;
using RtmpSharp.Net;
using RtmpSharp.Rpc;
using RtmpSharp.Service;

namespace RtmpSharp.Controller
{
    public enum PublishingType
    {
        Live,
        Record,
        Append
    }
    public class LivingController : AbstractController, IDisposable
    {

        class SessionStorage
        {
            public VideoData AVCConfigureRecord { get; set; } = null;
            public AudioData AACConfigureRecord { get; set; } = null;
            public Queue<AudioData> AudioBuffer { get; set; } = null;
            public Queue<VideoData> VideoBuffer { get; set; } = null;
            public Dictionary<string, ushort> PathToPusherClientId { get; set; } = new Dictionary<string, ushort>();
            public bool IsPublishing { get; set; }
            public PublishingType PublishingType { get; set; }
            public event AudioEventHandler AudioReceived;
            public event VideoEventHandler VideoReceived;
            public IStreamSession ConnectedSession { get; set; } = null;
            public NotifyAmf0 FlvMetaData { get; set; } = null;
            public void TriggerAudioReceived(object s, AudioEventArgs e)
            {
                AudioReceived?.Invoke(s, e);
            }
            public void TriggerVideoReceived(object s, VideoEventArgs e)
            {
                VideoReceived?.Invoke(s, e);
            }
        }

        private SessionStorage _sessionStorage = null;
        private PublisherSessionService _publisherSessionService = null;
        private int _streamId = 0;
        private Random _random = new Random();

        private const int COMMAND_CHANNEL = 4;
        private const int VIDEO_CHANNEL = 5;
        private const int AUDIO_CHANNEL = 6;

        public LivingController(PublisherSessionService publisherSessionService)
        {
            _publisherSessionService = publisherSessionService;
        }

        protected async Task<bool> _registerPlay(string path, ushort clientId)
        {
            return true;
        }

        [RpcMethod(Name = "@setDataFrame")]
        public void SetDataFrame(Command command)
        {
            if ((string)command.CommandObject != "onMetaData")
            {
                Console.WriteLine("Can only set metadata");
                throw new InvalidOperationException("Can only set metadata");
            }
            _sessionStorage.FlvMetaData = (NotifyAmf0)command;
        }

        [RpcMethod(Name = "publish")]
        public void Publish(string publishingName, string publishingType)
        {
            var publishingTypeMap = new Dictionary<string, PublishingType>()
            {
                { "live", PublishingType.Live },
                { "record", PublishingType.Record },
                { "append", PublishingType.Append }
            };
            if (string.IsNullOrEmpty(publishingName))
            {
                throw new InvalidOperationException("empty publishing name");
            }
            if (!publishingTypeMap.ContainsKey(publishingType))
            {
                throw new InvalidOperationException($"not supported publishing type {publishingType}");
            }

            _sessionStorage.PublishingType = publishingTypeMap[publishingType];

            _publisherSessionService.RegisterPublisher(publishingName, Session);
            Session.Disconnected += (s, e) =>
            {
                _publisherSessionService.RemovePublisher(Session);
            };

            Session.WriteProtocolControlMessage(new UserControlMessage(UserControlMessageType.StreamBegin, new int[] { _streamId }));

            Session.NotifyStatus(new AsObject
            {
                {"level", "status" },
                {"code", "NetStream.Publish.Start" },
                {"description", "Stream is now published." },
                {"details", publishingName }
            }, _streamId, COMMAND_CHANNEL);

            _sessionStorage.IsPublishing = true;
        }

        public void Dispose()
        {
            if (Session != null && Session.SessionStorage != null)
            {
                if (Session.SessionStorage.ConnectedSession != null)
                {
                    var publisherSessionStorage = Session.SessionStorage.ConnectedSession.SessionStorage as SessionStorage;
                    if (publisherSessionStorage != null)
                    {
                        publisherSessionStorage.VideoReceived -= BufferVideoData;
                        publisherSessionStorage.AudioReceived -= BufferAudioData;
                    }
                    _publisherSessionService.RemovePublisher(Session);
                }
            }
        }

        [RpcMethod(Name = "play")]
        public async Task Play(string streamName, double? start = null, double? duration = null, bool? reset = null)
        {
            if (!await _registerPlay(streamName, Session.ClientId))
            {
                throw new UnauthorizedAccessException();
            }

            Session.WriteProtocolControlMessage(new UserControlMessage(UserControlMessageType.StreamIsRecorded, new int[] { _streamId }));


            Session.NotifyStatus(new AsObject
            {
                {"level", "status" },
                {"code", "NetStream.Play.Reset" },
                {"description", "Resetting and playing stream." },
                {"details", streamName }
            }, _streamId, COMMAND_CHANNEL);
            Session.NotifyStatus(new AsObject
            {
                {"level", "status" },
                {"code", "NetStream.Play.Start" },
                {"description", "Started playing." },
                {"details", streamName }
            }, _streamId, COMMAND_CHANNEL);
            _sessionStorage.ConnectedSession = _publisherSessionService.FindPublisher(streamName);
            if (_sessionStorage.ConnectedSession == null)
            {
                throw new KeyNotFoundException("Request path Not Exists");
            }
            SendMetadata(streamName);
            var publisherSessionStorage = _sessionStorage.ConnectedSession.SessionStorage as SessionStorage;
            if (publisherSessionStorage == null)
            {
                throw new InvalidOperationException();
            }
            _sessionStorage.AudioBuffer = new Queue<AudioData>();
            _sessionStorage.VideoBuffer = new Queue<VideoData>();
            publisherSessionStorage.AudioReceived += BufferAudioData;
            publisherSessionStorage.VideoReceived += BufferVideoData;
            //ServePlay();
        }
        private SemaphoreSlim audioReceived = new SemaphoreSlim(0);
        private SemaphoreSlim videoReceived = new SemaphoreSlim(0);

        private void ServePlay()
        {
            var tsk = SendAvData();
            tsk.ContinueWith(t =>
            {
                if (!_sessionStorage.AudioBuffer.Any() && _sessionStorage.VideoBuffer.Any())
                {
                    Session.WriteProtocolControlMessage(new UserControlMessage(UserControlMessageType.StreamDry, new int[] { _streamId }));
                }
                ServePlay();
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        private async Task SendAvData()
        {
            await audioReceived.WaitAsync();
            await videoReceived.WaitAsync();
            Session.SendAmf0Data(_sessionStorage.AudioBuffer.Dequeue(), _streamId, AUDIO_CHANNEL);
            Session.SendAmf0Data(_sessionStorage.VideoBuffer.Dequeue(), _streamId, VIDEO_CHANNEL);
        }

        private bool readyForPlay = false;
        private int videoTimestamp = 0;
        private int audioTimestamp = 0;
        public override List<int> CreatedStreams { get; } = new List<int>();

        private void BufferAudioData(object sender, AudioEventArgs e)
        {
            if (!readyForPlay)
            {
                return;
            }
            // audioTimestamp += e.AudioData.Timestamp;
            // e.AudioData.Timestamp = audioTimestamp;
            Session.SendAmf0Data(e.AudioData, _streamId, AUDIO_CHANNEL);

            // _sessionStorage.AudioBuffer.Enqueue(e.AudioData);
            // audioReceived.Release();
        }
        private void BufferVideoData(object sender, VideoEventArgs e)
        {
            if (!readyForPlay && e.VideoData.Data[0] >> 4 == 0x01)
            {
                Session.WriteProtocolControlMessage(new UserControlMessage(UserControlMessageType.StreamBegin, new int[] { _streamId }));
                readyForPlay = true;
            }
            if (!readyForPlay)
            {
                return;
            }

            // _sessionStorage.VideoBuffer.Enqueue(e.VideoData);
            // videoReceived.Release();
            // _sessionStorage.VideoBuffer.Enqueue(e.VideoData);
            // var flvMetadata = (Dictionary<string, object>)_sessionStorage.ConnectedSession.SessionStorage.FlvMetaData.MethodCall.Parameters[0];
            // var frameCount = Math.Max(1, Session.BufferMilliseconds * ((double)flvMetadata["framerate"] / 1000));
            // while (_sessionStorage.VideoBuffer.Count >= frameCount)
            // {

            // videoTimestamp += e.VideoData.Timestamp;
            // e.VideoData.Timestamp = videoTimestamp;

            Session.SendAmf0Data(e.VideoData, _streamId, VIDEO_CHANNEL);
            // }
        }

        private void SendMetadata(string path, bool flvHeader = false)
        {
            var flvMetadata = (Dictionary<string, object>)_sessionStorage.ConnectedSession.SessionStorage.FlvMetaData.MethodCall.Parameters[0];
            var hasAudio = flvMetadata.ContainsKey("audiocodecid");
            var hasVideo = flvMetadata.ContainsKey("videocodecid");
            if (flvHeader)
            {
                var headerBuffer = Enumerable.Repeat<byte>(0x00, 13).ToArray<byte>();
                headerBuffer[0] = 0x46;
                headerBuffer[1] = 0x4C;
                headerBuffer[2] = 0x56;
                headerBuffer[3] = 0x01;
                byte hasAudioFlag = 0x01 << 2;
                byte has_video_flag = 0x01;
                byte typeFlag = 0x00;
                if (hasAudio) typeFlag |= hasAudioFlag;
                if (hasVideo) typeFlag |= has_video_flag;
                headerBuffer[4] = typeFlag;
                var dataOffset = BitConverter.GetBytes((uint)9);
                headerBuffer[5] = dataOffset[3];
                headerBuffer[6] = dataOffset[2];
                headerBuffer[7] = dataOffset[1];
                headerBuffer[8] = dataOffset[0];
                Session.SendRawData(headerBuffer);
            }
            Session.SendAmf0Data(_sessionStorage.ConnectedSession.SessionStorage.FlvMetaData, _streamId, COMMAND_CHANNEL);
            if (hasAudio) Session.SendAmf0Data(_sessionStorage.ConnectedSession.SessionStorage.AACConfigureRecord, _streamId, COMMAND_CHANNEL);
            if (hasVideo) Session.SendAmf0Data(_sessionStorage.ConnectedSession.SessionStorage.AVCConfigureRecord, _streamId, COMMAND_CHANNEL);

        }

        public override void OnAudio(AudioData audioData)
        {
            if (_sessionStorage.AACConfigureRecord == null && audioData.Data.Length >= 2)
            {
                _sessionStorage.AACConfigureRecord = audioData;
                return;
            }
            _sessionStorage.TriggerAudioReceived(this, new AudioEventArgs(audioData));
        }

        public override void OnVideo(VideoData videoData)
        {
            if (_sessionStorage.AVCConfigureRecord == null && videoData.Data.Length >= 2)
            {
                _sessionStorage.AVCConfigureRecord = videoData;
                return;
            }
            _sessionStorage.TriggerVideoReceived(this, new VideoEventArgs(videoData));
        }

        public override void EnsureSessionStorage()
        {
            if (Session.SessionStorage == null)
            {
                Session.SessionStorage = new SessionStorage();
            }
            _sessionStorage = Session.SessionStorage;
        }

        [RpcMethod(Name = "createStream", ChannelId = COMMAND_CHANNEL)]
        public override async Task<int> CreateStream()
        {
            if (_streamId != 0)
            {
                throw new InvalidOperationException("this controller only supprot one stream");
            }
            _streamId = _random.Next(1000, 4000);
            CreatedStreams.Add(_streamId);
            return _streamId;
        }

        public override void DeleteStream()
        {
            CreatedStreams.Remove(_streamId);
            _streamId = 0;
        }
    }
}
