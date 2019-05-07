using System;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Threading.Tasks;
using Complete;
using RtmpSharp.IO;
using RtmpSharp.Messaging.Events;

namespace RtmpSharp.Controller
{
    public class LivingController : AbstractController
    {
        class SessionStorage
        {
            public VideoData AVCConfigureRecord = null;
            public AudioData AACConfigureRecord = null;
            public Queue<AudioData> AudioBuffer = new Queue<AudioData>();
            public Queue<VideoData> VideoBuffer = new Queue<VideoData>();
            public Dictionary<string, ushort> PathToPusherClientId = new Dictionary<string, ushort>();
        }

        private SessionStorage _sessionStorage = null;

        public LivingController()
        {
        }
        private async Task<bool> _registerPlay(string path, ushort clientId)
        {
            return true;
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

            SendMetadata(path);
            server.ConnectToClient(_app, path, ClientId, ChannelType.Video);
            server.ConnectToClient(_app, path, ClientId, ChannelType.Audio);
        }

        private void SendMetadata(string path)
        {
            if (!_sessionStorage.PathToPusherClientId.TryGetValue(path, out var client_id)) throw new KeyNotFoundException("Request Path Not Found");
            if (!connects.TryGetValue(client_id, out var state))
            {
                PathToPusherClientId.Remove(path);
                throw new KeyNotFoundException("Request Client Not Exists");
            }
            connect = state.Connect;
            if (connect.IsPublishing)
            {
                var flv_metadata = (Dictionary<string, object>)connect.FlvMetaData.MethodCall.Parameters[0];
                var has_audio = flv_metadata.ContainsKey("audiocodecid");
                var has_video = flv_metadata.ContainsKey("videocodecid");
                if (flvHeader)
                {
                    var header_buffer = Enumerable.Repeat<byte>(0x00, 13).ToArray<byte>();
                    header_buffer[0] = 0x46;
                    header_buffer[1] = 0x4C;
                    header_buffer[2] = 0x56;
                    header_buffer[3] = 0x01;
                    byte has_audio_flag = 0x01 << 2;
                    byte has_video_flag = 0x01;
                    byte type_flag = 0x00;
                    if (has_audio) type_flag |= has_audio_flag;
                    if (has_video) type_flag |= has_video_flag;
                    header_buffer[4] = type_flag;
                    var data_offset = BitConverter.GetBytes((uint)9);
                    header_buffer[5] = data_offset[3];
                    header_buffer[6] = data_offset[2];
                    header_buffer[7] = data_offset[1];
                    header_buffer[8] = data_offset[0];
                    self.SendRawData(header_buffer);
                }
                self.SendAmf0Data(connect.FlvMetaData);
                if (has_audio) self.SendAmf0Data(connect.AACConfigureRecord);
                if (has_video) self.SendAmf0Data(connect.AvCConfigureRecord);
            }
        }

        internal override void OnAudio(AudioData audioData)
        {
            if (_sessionStorage.AACConfigureRecord != null && audioData.Data.Length >= 2 && audioData.Data[1] == 0)
            {
                _sessionStorage.AACConfigureRecord = audioData;
                return;
            }
            _sessionStorage.AudioBuffer.Enqueue(audioData);
        }

        internal override void OnVideo(VideoData videoData)
        {
            if (Session.SessionStorage.AVCConfigureRecord != null && videoData.Data.Length >= 2 && videoData.Data[1] == 0)
            {
                Session.SessionStorage.AVCConfigureRecord = videoData;
                return;
            }
            Session.SessionStorage.VideoBuffer.Enqueue(videoData);
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