namespace Engine.Core;

public sealed class ServiceRegistry : IServiceRegistry
{
    private sealed record ServiceEntry(Func<IServiceRegistry, object> Factory, Lifetime Lifetime);

    private readonly object _lock = new();
    private readonly Dictionary<Type, ServiceEntry> _entries = [];
    private readonly Dictionary<Type, object> _singletons = [];

    public void RegisterSingleton<TService>(TService instance) where TService : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        RegisterCore(typeof(TService), _ => instance, Lifetime.Singleton);
    }

    public void RegisterSingleton<TService>(Func<IServiceRegistry, TService> factory) where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        RegisterCore(typeof(TService), registry => factory(registry), Lifetime.Singleton);
    }

    public void RegisterTransient<TService>(Func<IServiceRegistry, TService> factory) where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        RegisterCore(typeof(TService), registry => factory(registry), Lifetime.Transient);
    }

    public bool TryResolve<TService>(out TService service) where TService : class
    {
        object? resolved;
        lock (_lock)
        {
            if (!_entries.TryGetValue(typeof(TService), out ServiceEntry? entry))
            {
                service = null!;
                return false;
            }
            if (entry.Lifetime == Lifetime.Singleton && _singletons.TryGetValue(typeof(TService), out object? singleton))
            {
                resolved = singleton;
            }
            else
            {
                resolved = entry.Factory(this);
                if (entry.Lifetime == Lifetime.Singleton)
                    _singletons[typeof(TService)] = resolved;
            }
        }
        service = (TService)resolved!;
        return true;
    }

    public TService Resolve<TService>() where TService : class
    {
        bool resolved = TryResolve(out TService service);
        if (!resolved)
            throw new InvalidOperationException($"Service '{typeof(TService).Name}' is not registered.");
        return service;
    }

    public bool Unregister<TService>() where TService : class
    {
        lock (_lock)
        {
            bool removed = _entries.Remove(typeof(TService));
            _singletons.Remove(typeof(TService));
            return removed;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _singletons.Clear();
        }
    }

    private void RegisterCore(Type serviceType, Func<IServiceRegistry, object> factory, Lifetime lifetime)
    {
        lock (_lock)
        {
            _entries[serviceType] = new ServiceEntry(factory, lifetime);
            _singletons.Remove(serviceType);
        }
    }
}
