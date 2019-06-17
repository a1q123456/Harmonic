using System.Collections.Generic;
using System.Threading.Tasks;
using RtmpSharp.Messaging.Events;
using RtmpSharp.Net;
using RtmpSharp.Rpc;

namespace RtmpSharp.Controller
{
    public abstract class AbstractController
    {
        public IStreamSession Session { get; set; } = null;
        public abstract List<int> CreatedStreams { get; }
        public virtual void OnVideo(VideoData data) { }
        public virtual void OnAudio(AudioData data) { }
        public virtual void EnsureSessionStorage() {}

        [RpcMethod(Name="createStream")]
        public abstract Task<int> CreateStream();
        [RpcMethod(Name="deleteStream")]
        public abstract void DeleteStream();
        
    }
}
