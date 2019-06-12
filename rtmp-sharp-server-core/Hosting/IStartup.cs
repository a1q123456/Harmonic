using Autofac;

namespace RtmpSharp.Hosting
{
    public interface IStartup
    {
        void ConfigureServices(ContainerBuilder builder);
    }
}