using System;
using Autofac;

namespace RtmpSharp.Hosting
{
    public interface IStartup
    {
        void ConfigureServices(ContainerBuilder builder);

        Type[] SessionScopedServices { get; }
    }
}