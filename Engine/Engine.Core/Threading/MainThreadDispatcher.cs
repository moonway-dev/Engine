using System.Collections.Concurrent;

namespace Engine.Core;

public sealed class MainThreadDispatcher
{
    private readonly ConcurrentQueue<Action> _queue = new();

    public int PendingCount => _queue.Count;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _queue.Enqueue(action);
    }

    public void Process()
    {
        while (_queue.TryDequeue(out Action? action))
            action();
    }
}
