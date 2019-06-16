using RtmpSharp.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RtmpSharp.Controller;
using RtmpSharp.Hosting;

namespace demo
{
    class Program
    {
        static void Main(string[] args)
        {
            RtmpServer server = new RtmpServer(new Startup(), new RtmpSharp.IO.SerializationContext());
            server.RegisterController<LivingController>();
            var tsk = server.StartAsync();
            tsk.Wait();
        }
    }
}
