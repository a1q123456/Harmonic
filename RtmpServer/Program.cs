using RtmpServer;

var builder = WebApplication.CreateSlimBuilder(args);

var sc = builder.Services;
//sc.AddHostedService<SocketServer>(_ => new SocketServer(new IPEndPoint(IPAddress.Parse("0.0.0.0"), 1937)));
//sc.AddHostedService<SocketClient>(_ => new SocketClient(new IPEndPoint(IPAddress.Parse("0.0.0.0"), 1937)));
sc.AddHostedService<RateLimitTester>();
var app = builder.Build();

var sampleTodos = TodoGenerator.GenerateTodos().ToArray();

var todosApi = app.MapGroup("/todos");
todosApi.MapGet("/", () => sampleTodos);
todosApi.MapGet("/{id}", (int id) =>
    sampleTodos.FirstOrDefault(a => a.Id == id) is { } todo
        ? Results.Ok(todo)
        : Results.NotFound());

app.Run();
