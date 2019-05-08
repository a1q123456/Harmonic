using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Threading.Tasks;
using Complete;
using RtmpSharp.IO;
using RtmpSharp.Messaging.Events;
using RtmpSharp.Net;

namespace RtmpSharp.Controller
{
    public class LivingController : AbstractController
    {
        class SessionStorage
        {
            public VideoData AVCConfigureRecord { get; set; } = null;
            public AudioData AACConfigureRecord { get; set; } = null;
            public Queue<AudioData> AudioBuffer { get; set; } = null;
            public Queue<VideoData> VideoBuffer { get; set; } = null;
            public Dictionary<string, ushort> PathToPusherClientId { get; set; } = new Dictionary<string, ushort>();
            public bool IsPublishing { get; set; }
            public event EventHandler AudioReceived;
            public event EventHandler VideoReceived;
            public long BufferedFrames { get; set; } = 1;
            public IStreamSession ConnectedSession { get; set; } = null;
            public NotifyAmf0 FlvMetaData { get; set; } = null;
            public void TriggerAudioReceived(object s, EventArgs e)
            {
                AudioReceived?.Invoke(s, e);
            }
            public void TriggerVideoReceived(object s, EventArgs e)
            {
                VideoReceived?.Invoke(s, e);
            }
        }

        private SessionStorage _sessionStorage = null;

        public LivingController()
        {
        }
        
        private async Task<bool> _registerPlay(string path, ushort clientId)
        {
            return true;
        }
        
        public void @setDataFrame(Command command)
        {
            if ((string)command.ConnectionParameters != "onMetaData")
            {
                Console.WriteLine("Can only set metadata");
                throw new InvalidOperationException("Can only set metadata");
            }
            _sessionStorage.FlvMetaData = (NotifyAmf0)command;
        }
        
        public void publish(Command command)
        {
            _sessionStorage.AudioBuffer = new Queue<AudioData>();
            _sessionStorage.VideoBuffer = new Queue<VideoData>();
        }
        
        public async Task play(Command command)
        {
            string path = (string)command.MethodCall.Parameters[0];
            if (!await _registerPlay(path, Session.ClientId))
            {
                throw new UnauthorizedAccessException();
            }

            Session.WriteProtocolControlMessage(new UserControlMessage(UserControlMessageType.StreamBegin, new int[] { Session.StreamId }));

            Session.NotifyStatus(new AsObject
            {
                {"level", "status" },
                {"code", "NetStream.Play.Reset" },
                {"description", "Resetting and playing stream." },
                {"details", path }
            });
            Session.NotifyStatus(new AsObject
            {
                {"level", "status" },
                {"code", "NetStream.Play.Start" },
                {"description", "Started playing." },
                {"details", path }
            });
            _sessionStorage.ConnectedSession = Session.Server.FindSession(path).FirstOrDefault(s => s.SessionStorage.IsPublishing);
            if (_sessionStorage == null)
            {
                throw new KeyNotFoundException("Request path Not Exists");
            }
            SendMetadata(path);
            _sessionStorage.AudioBuffer = new Queue<AudioData>(_sessionStorage.ConnectedSession.SessionStorage.AudioBuffer);
            _sessionStorage.VideoBuffer = new Queue<VideoData>(_sessionStorage.ConnectedSession.SessionStorage.VideoBuffer);
            var publisherSessionStorage = _sessionStorage.ConnectedSession.SessionStorage as SessionStorage;
            if (publisherSessionStorage == null)
            {
                throw new InvalidOperationException();
            }
            publisherSessionStorage.AudioReceived += (s, e) =>
            {
                _sessionStorage.AudioBuffer.Enqueue(publisherSessionStorage.AudioBuffer.Peek());
            };
            publisherSessionStorage.VideoReceived += (s, e) =>
            {
                _sessionStorage.VideoBuffer.Enqueue(publisherSessionStorage.VideoBuffer.Peek());
            };
            Session.Server.IOLoop.AddCallback(() =>
            {
                if (!_sessionStorage.AudioBuffer.Any() || _sessionStorage.VideoBuffer.Any())
                {
                    Session.WriteProtocolControlMessage(new UserControlMessage(UserControlMessageType.StreamDry), new int[] { Session.StreamId });
                    return;
                }
                Session.SendAmf0Data(_sessionStorage.AudioBuffer.Dequeue());
                Session.SendAmf0Data(_sessionStorage.VideoBuffer.Dequeue());
            }, IOLoopCallbackOptions.EveryLoop);
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
            Session.SendAmf0Data(_sessionStorage.ConnectedSession.SessionStorage.FlvMetaData);
            if (hasAudio) Session.SendAmf0Data(_sessionStorage.ConnectedSession.SessionStorage.AACConfigureRecord);
            if (hasVideo) Session.SendAmf0Data(_sessionStorage.ConnectedSession.SessionStorage.AvCConfigureRecord);
            
        }

        internal override void OnAudio(AudioData audioData)
        {
            if (_sessionStorage.AACConfigureRecord != null && audioData.Data.Length >= 2 && audioData.Data[1] == 0)
            {
                _sessionStorage.AACConfigureRecord = audioData;
                return;
            }
            _sessionStorage.TriggerAudioReceived(this, new EventArgs());
            _sessionStorage.AudioBuffer.Enqueue(audioData);
            while (_sessionStorage.AudioBuffer.Count > _sessionStorage.BufferedFrames)
            {
                _sessionStorage.AudioBuffer.Dequeue();
            }
        }

        internal override void OnVideo(VideoData videoData)
        {
            if (_sessionStorage.AVCConfigureRecord != null && videoData.Data.Length >= 2 && videoData.Data[1] == 0)
            {
                _sessionStorage.AVCConfigureRecord = videoData;
                return;
            }
            _sessionStorage.VideoBuffer.Enqueue(videoData);
            _sessionStorage.TriggerVideoReceived(this, new EventArgs());
            while (_sessionStorage.VideoBuffer.Count > _sessionStorage.BufferedFrames)
            {
                _sessionStorage.VideoBuffer.Dequeue();
            }
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
