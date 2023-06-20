using Harmonic.Hosting;
using System;
using System.Net;

namespace demo;

class Program
{
    static void Main(string[] args)
    {
        RtmpServer server = new RtmpServerBuilder()
            .UseStartup<Startup>()
            .UseWebSocket(c =>
            {
                c.BindEndPoint = new IPEndPoint(IPAddress.Parse("0.0.0.0"), 8080);
            })
            .Build();
        var tsk = server.StartAsync();
        tsk.Wait();
    }
}