#rtmp-sharp-with-server
fork of [rtmp-sharp](https://github.com/imiuka/rtmp-sharp)

#usage

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