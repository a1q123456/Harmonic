using System;
using Autofac;
using RtmpSharp.Hosting;

class Startup : IStartup
{
    public void ConfigureServices(ContainerBuilder builder)
    {
    }
    public Type[] SessionScopedServices { get; } = new Type[0] {};
}