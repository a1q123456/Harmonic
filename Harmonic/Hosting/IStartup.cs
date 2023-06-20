using System;
using Autofac;

namespace Harmonic.Hosting;

public interface IStartup
{
    void ConfigureServices(ContainerBuilder builder);

}