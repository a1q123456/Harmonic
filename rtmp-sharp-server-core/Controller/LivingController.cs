using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Threading.Tasks;
using Complete;
using RtmpSharp.IO;
using RtmpSharp.Messaging;
using RtmpSharp.Messaging.Events;
using RtmpSharp.Net;
using RtmpSharp.Service;

namespace RtmpSharp.Controller
{
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
            public event AudioEventHandler AudioReceived;
            public event VideoEventHandler VideoReceived;
            public long BufferFrames { get; set; } = 1;
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

        public LivingController(PublisherSessionService publisherSessionService)
        {
            _publisherSessionService = publisherSessionService;
        }
        
        protected async Task<bool> _registerPlay(string path, ushort clientId)
        {
            return true;
        }
        
        public void @setDataFrame(Command command)
        {
            if ((string)command.CommandObject != "onMetaData")
            {
                Console.WriteLine("Can only set metadata");
                throw new InvalidOperationException("Can only set metadata");
            }
            _sessionStorage.FlvMetaData = (NotifyAmf0)command;
        }
        
        public void publish(string publishingName, string publishingType)
        {
            _sessionStorage.AudioBuffer = new Queue<AudioData>();
            _sessionStorage.VideoBuffer = new Queue<VideoData>();
            _publisherSessionService.RegisterPublisher(Session.ConnectionInformation.TcUrl, Session);
            Session.Disconnected += (s, e) =>
            {
                _publisherSessionService.RemovePublisher(Session);
            };
        }

        public void Dispose()
        {
            if (Session != null)
            {
                _publisherSessionService.RemovePublisher(Session);
            }
        }
        
        public async Task<ushort> createStream()
        {
            return Session.StreamId;
        }

        public async Task play(Command command)
        {
            string path = (string)command.MethodCall.Parameters[0];
            if (!await _registerPlay(path, Session.ClientId))
            {
                throw new UnauthorizedAccessException();
            }

            await Session.WriteProtocolControlMessageAsync(new UserControlMessage(UserControlMessageType.StreamBegin, new int[] { Session.StreamId }));

            await Session.NotifyStatusAsync(new AsObject
            {
                {"level", "status" },
                {"code", "NetStream.Play.Reset" },
                {"description", "Resetting and playing stream." },
                {"details", path }
            });
            await Session.NotifyStatusAsync(new AsObject
            {
                {"level", "status" },
                {"code", "NetStream.Play.Start" },
                {"description", "Started playing." },
                {"details", path }
            });
            _sessionStorage.ConnectedSession = _publisherSessionService.FindPublisher(path);
            if (_sessionStorage == null)
            {
                throw new KeyNotFoundException("Request path Not Exists");
            }
            SendMetadata(path);
            var publisherSessionStorage = _sessionStorage.ConnectedSession.SessionStorage as SessionStorage;
            if (publisherSessionStorage == null)
            {
                throw new InvalidOperationException();
            }
            _sessionStorage.AudioBuffer = new Queue<AudioData>(publisherSessionStorage.AudioBuffer);
            _sessionStorage.VideoBuffer = new Queue<VideoData>(publisherSessionStorage.VideoBuffer);
            publisherSessionStorage.AudioReceived += (s, e) =>
            {
                _sessionStorage.AudioBuffer.Enqueue(e.AudioData);
            };
            publisherSessionStorage.VideoReceived += (s, e) =>
            {
                _sessionStorage.VideoBuffer.Enqueue(e.VideoData);
            };
            ServePlay();
        }

        private async void ServePlay()
        {
            if (!_sessionStorage.AudioBuffer.Any() || _sessionStorage.VideoBuffer.Any())
            {
                await Session.WriteProtocolControlMessageAsync(new UserControlMessage(UserControlMessageType.StreamDry, new int[] { Session.StreamId }));
                await Task.Delay(10);
            }
            else
            {
                await Session.SendAmf0DataAsync(_sessionStorage.AudioBuffer.Dequeue());
                await Session.SendAmf0DataAsync(_sessionStorage.VideoBuffer.Dequeue());
            }
            ServePlay();
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
            Session.SendAmf0DataAsync(_sessionStorage.ConnectedSession.SessionStorage.FlvMetaData);
            if (hasAudio) Session.SendAmf0DataAsync(_sessionStorage.ConnectedSession.SessionStorage.AACConfigureRecord);
            if (hasVideo) Session.SendAmf0DataAsync(_sessionStorage.ConnectedSession.SessionStorage.AvCConfigureRecord);
            
        }

        internal override void OnAudio(AudioData audioData)
        {
            if (_sessionStorage.AACConfigureRecord != null && audioData.Data.Length >= 2 && audioData.Data[1] == 0)
            {
                _sessionStorage.AACConfigureRecord = audioData;
                return;
            }
            _sessionStorage.AudioBuffer.Enqueue(audioData);
            while (_sessionStorage.AudioBuffer.Count > _sessionStorage.BufferFrames)
            {
                _sessionStorage.AudioBuffer.Dequeue();
            }
            _sessionStorage.TriggerAudioReceived(this, new AudioEventArgs(audioData));
        }

        internal override void OnVideo(VideoData videoData)
        {
            
            if (_sessionStorage.AVCConfigureRecord != null && videoData.Data.Length >= 2 && videoData.Data[1] == 0)
            {
                _sessionStorage.AVCConfigureRecord = videoData;
                return;
            }
            _sessionStorage.VideoBuffer.Enqueue(videoData);
            while (_sessionStorage.VideoBuffer.Count > _sessionStorage.BufferFrames)
            {
                _sessionStorage.VideoBuffer.Dequeue();
            }
            _sessionStorage.TriggerVideoReceived(this, new VideoEventArgs(videoData));
        }

        internal override void EnsureSessionStorage()
        {
            if (Session.SessionStorage == null)
            {
                Session.SessionStorage = new SessionStorage();
            }
            _sessionStorage = Session.SessionStorage;
        }
    }
}
