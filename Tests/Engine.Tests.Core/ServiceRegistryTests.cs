using Engine.Core;
using Xunit;

namespace Engine.Tests.Core;

public class ServiceRegistryTests
{
    private sealed class Service { }
    private sealed class Other { }

    [Fact]
    public void Singleton_ReturnsSameInstance()
    {
        ServiceRegistry registry = new();
        Service instance = new();
        registry.RegisterSingleton<Service>(instance);
        Assert.Same(instance, registry.Resolve<Service>());
        Assert.Same(instance, registry.Resolve<Service>());
    }

    [Fact]
    public void SingletonFactory_InvokedOnce()
    {
        ServiceRegistry registry = new();
        int creations = 0;
        registry.RegisterSingleton(_ =>
        {
            creations++;
            return new Service();
        });
        registry.Resolve<Service>();
        registry.Resolve<Service>();
        Assert.Equal(1, creations);
    }

    [Fact]
    public void Transient_ReturnsNewInstanceEachTime()
    {
        ServiceRegistry registry = new();
        registry.RegisterTransient(_ => new Service());
        Service first = registry.Resolve<Service>();
        Service second = registry.Resolve<Service>();
        Assert.NotSame(first, second);
    }

    [Fact]
    public void TryResolve_FalseForMissingService()
    {
        ServiceRegistry registry = new();
        Assert.False(registry.TryResolve<Service>(out _));
        Assert.Throws<InvalidOperationException>(() => registry.Resolve<Service>());
    }

    [Fact]
    public void Unregister_RemovesService()
    {
        ServiceRegistry registry = new();
        registry.RegisterSingleton(new Service());
        Assert.True(registry.Unregister<Service>());
        Assert.False(registry.TryResolve<Service>(out _));
    }
}
