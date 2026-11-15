using System.Collections.Concurrent;
using System.Reflection;

namespace Engine.Core;

public sealed class EventBus : IEventBus
{
    private readonly object _lock = new();
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];
    private readonly ConcurrentQueue<object> _deferred = new();

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_lock)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out List<Delegate>? handlers))
            {
                handlers = [];
                _handlers[typeof(TEvent)] = handlers;
            }
            handlers.Add(handler);
        }
        return new Subscription(this, typeof(TEvent), handler);
    }

    public void Unsubscribe<TEvent>(Action<TEvent> handler) =>
        UnsubscribeCore(typeof(TEvent), handler);

    public void Publish<TEvent>(TEvent evt)
    {
        Delegate[] handlers = SnapshotHandlers(typeof(TEvent));
        List<Exception>? failures = null;
        foreach (Delegate handler in handlers)
        {
            try
            {
                ((Action<TEvent>)handler)(evt);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }
        if (failures is not null)
            throw new AggregateException("One or more event handlers failed.", failures);
    }

    public void PublishDeferred<TEvent>(TEvent evt) =>
        _deferred.Enqueue(evt!);

    public void ProcessDeferred()
    {
        while (_deferred.TryDequeue(out object? evt))
        {
            Delegate[] handlers = SnapshotHandlers(evt.GetType());
            List<Exception>? failures = null;
            foreach (Delegate handler in handlers)
            {
                try
                {
                    handler.DynamicInvoke(evt);
                }
                catch (TargetInvocationException invocationException) when (invocationException.InnerException is not null)
                {
                    failures ??= [];
                    failures.Add(invocationException.InnerException);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    failures ??= [];
                    failures.Add(exception);
                }
            }
            if (failures is not null)
                throw new AggregateException("One or more deferred event handlers failed.", failures);
        }
    }

    private Delegate[] SnapshotHandlers(Type eventType)
    {
        lock (_lock)
        {
            if (!_handlers.TryGetValue(eventType, out List<Delegate>? handlers) || handlers.Count == 0)
                return [];
            return [.. handlers];
        }
    }

    private void UnsubscribeCore(Type eventType, Delegate handler)
    {
        lock (_lock)
        {
            if (!_handlers.TryGetValue(eventType, out List<Delegate>? handlers))
                return;
            handlers.Remove(handler);
            if (handlers.Count == 0)
                _handlers.Remove(eventType);
        }
    }

    private sealed class Subscription(EventBus bus, Type eventType, Delegate handler) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            bus.UnsubscribeCore(eventType, handler);
        }
    }
}
