namespace Engine.Core;

public enum Lifetime
{
    Singleton,
    Transient,
}

public interface IServiceRegistry
{
    void RegisterSingleton<TService>(TService instance) where TService : class;

    void RegisterSingleton<TService>(Func<IServiceRegistry, TService> factory) where TService : class;

    void RegisterTransient<TService>(Func<IServiceRegistry, TService> factory) where TService : class;

    bool TryResolve<TService>(out TService service) where TService : class;

    TService Resolve<TService>() where TService : class;

    bool Unregister<TService>() where TService : class;

    void Clear();
}
