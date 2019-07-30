using Harmonic.Hosting;
using System;

namespace demo
{
    class Program
    {
        static void Main(string[] args)
        {
            RtmpServer server = new RtmpServerBuilder().UseStartup<Startup>().Build();
            var tsk = server.StartAsync();
            tsk.Wait();

        }
    }
}
