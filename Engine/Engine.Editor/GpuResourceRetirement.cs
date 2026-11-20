namespace Engine.Editor;

public sealed class GpuResourceRetirement : IDisposable
{
    private readonly List<IDisposable>[] _slots;
    private readonly List<IDisposable> _pending = [];

    public GpuResourceRetirement(int framesInFlight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(framesInFlight);
        _slots = Enumerable.Range(0, framesInFlight).Select(_ => new List<IDisposable>()).ToArray();
    }

    public void Retire(IDisposable resource) => _pending.Add(resource);

    public void BeginFrame(int slot)
    {
        foreach (IDisposable resource in _slots[slot])
            resource.Dispose();
        _slots[slot].Clear();
        _slots[slot].AddRange(_pending);
        _pending.Clear();
    }

    public void Dispose()
    {
        foreach (IDisposable resource in _pending)
            resource.Dispose();
        _pending.Clear();
        foreach (List<IDisposable> slot in _slots)
        {
            foreach (IDisposable resource in slot)
                resource.Dispose();
            slot.Clear();
        }
    }
}
