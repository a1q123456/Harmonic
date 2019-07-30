master is not a stable branch, you may want to see the latest [tag](https://github.com/a1q123456/rtmp-sharp-server/tree/v0.0.1)

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

# Expansibility

Harmonic will scan your assembly and try to find classes that inherit from `RtmpController` or `WebSocketController` then register them into Harmonic, and map controller by url `rtmp://<address>/<controller_name>/<streamName>` for rtmp and `ws://<address>/<controller_name>/<streamName>`. the controller_name is controller class's name then remove the `Controller` suffix, for example `Living` is controller_name of `LivingController`.

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
            url: "rtmp://127.0.0.1/living/streamName"
        });
        flvPlayer.attachMediaElement(player);
        flvPlayer.load();
        flvPlayer.play();
    }
</script>
```



```
