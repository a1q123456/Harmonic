#rtmp-sharp-with-server
forked from [rtmp-sharp](https://github.com/imiuka/rtmp-sharp)

#Usage

```csharp
RtmpServer server = new RtmpServer(new RtmpSharp.IO.SerializationContext());
server.RegisterApp("app");
server.Start();
```

to start websocket server, you need to set bindWebsocketPort parameter
```csharp
RtmpServer server = new RtmpServer(new RtmpSharp.IO.SerializationContext(), bindWebsocketPort: 80);
```

you may want to authenticate user when publishing or playing
```csharp
RtmpServer server = new RtmpServer(new RtmpSharp.IO.SerializationContext(), publishParameterAuth: (app, namevalue) => true, playParameterAuth: (app, namevalue) => true);
```

you can rewrite RtmpConnect or WebsocketConnect to implement your own service logic


#Test

## push video file using ffmpeg
```bash
ffmpeg -i test.mp4 -f flv -vcodec h264 -acodec aac "rtmp://127.0.0.1/app/live"
```
## play rtmp stream using ffplay

```bash
ffplay "rtmp://127.0.0.1/app/live"
```

## play flv stream using [flv.js](https://github.com/Bilibili/flv.js) over websocket

```html
<video id="player"></video>

<script>

    if (flvjs.isSupported()) {
        var player = document.getElementById('player');
        var flvPlayer = flvjs.createPlayer({
            type: 'flv',
            url: 'ws://127.0.0.1:80/app/live'
        });
        flvPlayer.attachMediaElement(player);
        flvPlayer.load();
        flvPlayer.play();
    }
</script>
```



```
