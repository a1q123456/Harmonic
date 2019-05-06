using RtmpSharp.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace demo
{
    class Program
    {
        static void Main(string[] args)
        {
            RtmpServer server = new RtmpServer(new RtmpSharp.IO.SerializationContext());
            //server.RegisterController("app");
            using (var cts = new CancellationTokenSource())
            {
                var tsk = server.StartAsync(cts.Token);
                tsk.Wait();
            }
        }
    }
}
