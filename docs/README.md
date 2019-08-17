# Harmonic
A high performance RTMP live streaming application framework


# Usage


Program.cs

```csharp
using Harmonic.Hosting;
using System;
using System.Net;

namespace demo
{
    class Program
    {
        static void Main(string[] args)
        {
            RtmpServer server = new RtmpServerBuilder()
                .UseStartup<Startup>()
                .Build();
            var tsk = server.StartAsync();
            tsk.Wait();
        }
    }
}

```

StartUp.cs
```csharp
using Autofac;
using Harmonic.Hosting;

namespace demo
{
    class Startup : IStartup
    {
        public void ConfigureServices(ContainerBuilder builder)
        {

        }
    }
}

```

Build a server like this to support websocket-flv transmission

```csharp
RtmpServer server = new RtmpServerBuilder()
    .UseStartup<Startup>()
    .UseWebSocket(c =>
    {
        c.BindEndPoint = new IPEndPoint(IPAddress.Parse("0.0.0.0"), 8080);
    })
    .Build();

```
# Scalability

Harmonic will scan your assembly and try to find classes that inherit from `RtmpController` or `WebSocketController` then register them into Harmonic, and map controller by url `rtmp://<address>/<controller_name>/<streamName>` for rtmp and `ws://<address>/<controller_name>/<streamName>`. the controller_name is controller class's name then remove the `Controller` suffix, for example `Living` is controller_name of `LivingController`. once Harmonic found any class that inherts from `RtmpController` or `WebSocketController`, it will never register `RtmpController` and `WebSocketController`.

You can also inherit builtin classes `LivingController` or `WebSocketPlayController`, when Harmonic found a class that inherit from them, it will not register `LivingController` and `WebSocketPlayController`. When you want to custom streaming logic, you can create a class that inherits from `LivingController` or `WebSocketPlayController`.

```csharp
public class MyLivingController : LivingController
{
    [RpcMethod("createStream")]
    public new uint CreateStream()
    {
        var stream = RtmpSession.CreateNetStream<MyLivingStream>();
        return stream.MessageStream.MessageStreamId;
    }
}

public class MyLivingStream : LivingStream
{
    [RpcMethod(Name = "publish")]
    public void Publish([FromOptionalArgument] string publishingName, [FromOptionalArgument] string publishingType)
    {
        if (...)
        {
            
        }
        // your logic

        base.Publish(publishingName, publishingType);
        
    }
}

```

## RtmpController and WebSocketController
RtmpController and WebSocketController are two abstract basic controller, they are intended for serving video on rtmp protocol and websocket protocol.
When a controller class inherit from RtmpController, it will become an rtmp controller, it will working on rtmp protocol, and supports every rtmp features.
When a controller class inherit from WebSocketController, it will become a websocket controller, it can only send flv header and tags.

## Recording
The `RecordController` can record video, by default, it will save flv files into `working_dir/Record`.
You can overrite the recording configuration by register you own configure class in `StartUp` class

```csharp
class MyRecordConfiguration: RecordServiceConfiguration
{
    public override string RecordPath { get; set; } = @"MyRecordPath";
    public override string FilenameFormat { get; set; } = @"recorded-{streamName}";
};

class Startup : IStartup
{
    public void ConfigureServices(ContainerBuilder builder)
    {
        builder.Register(c => new MyRecordConfiguration()).As<RecordServiceConfiguration>();
    }
}
```

## Websocket
websocket protocol and rtmp protocol are running on two different controllers, so when you push vide to url: `rtmp://127.0.0.1/living/a`, the corresponding playing url for websocket is `ws://127.0.0.1/websocketplay/a`

## Internal Controllers

### LivingController
LivingController provides a simple living service, it recieves video or audio data and broadcast data to other plays.

### RecordController
RecordController supports video recording, and can be configured.

### WebsocketPlayController
WebsocketPlayController supports two modes: lving mode and vod mode. when stream name in url is not in living, this controller will try to find a stream in recording folder, then play it.

## Internal Classes

### NetConnection
NetConnection is responsible for managing all NetStreams, process some control messages, rpc support, handle `connect`, `createStream` and another command messages.

### NetStream
NetStream is created by NetConnection, it reperents a logic stream, all of RtmpController is NetStream.

### MessageStream
MessageStream repersents a logic rtmp stream. Every message must be sent on a specific MessageStream.

### ChunkStream
Message will be break into chunks before sending to peer. chunks must be send on a ChunkStream, in a ChunkStream, every chunk must be sent one by one. that means you can't send a message concurrently on one ChunkStream, but message can be sent concurrently on some different ChunkStream.

### RtmpSession
RtmpSession is a bridge from NetStream to RtmpServer, controllers can access to it's own RtmpSession property to send message, or close connection or something else.

## Rpc
See [rpc-docs](rpc.md)

## Api
See [api-docs](api.md)

# Test

## push video file using ffmpeg
```bash
ffmpeg -i test.mp4 -f flv -vcodec h264 -acodec aac "rtmp://127.0.0.1/living/streamName"
```
## play rtmp stream using ffplay

```bash
ffplay "rtmp://127.0.0.1/living/streamName"
```

## play flv stream using [flv.js](https://github.com/Bilibili/flv.js) by websocket

```html
<video id="player"></video>

<script>

    if (flvjs.isSupported()) {
        var player = document.getElementById('player');
        var flvPlayer = flvjs.createPlayer({
            type: 'flv',
            url: "ws://127.0.0.1/websocketplay/streamName"
        });
        flvPlayer.attachMediaElement(player);
        flvPlayer.load();
        flvPlayer.play();
    }
</script>
```



```
