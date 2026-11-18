namespace Engine.Backends.Vulkan;

internal sealed class VulkanQuarantine<TKey, TResource> where TKey : notnull
{
    private readonly HashSet<TKey> _keys = [];
    private readonly List<(TKey Key, TResource Resource)> _resources = [];

    public int Count => _resources.Count;

    public bool TryAdd(TKey key, TResource resource)
    {
        if (!_keys.Add(key))
            return false;
        try
        {
            _resources.Add((key, resource));
            return true;
        }
        catch
        {
            _keys.Remove(key);
            throw;
        }
    }

    public void DrainAfterConfirmedIdle(bool deviceIdleConfirmed, Action<TResource> release)
    {
        if (!deviceIdleConfirmed)
            throw new InvalidOperationException("Quarantined resources can only be released after confirmed device idle.");
        ArgumentNullException.ThrowIfNull(release);
        for (int i = 0; i < _resources.Count;)
        {
            (TKey key, TResource resource) = _resources[i];
            release(resource);
            _resources.RemoveAt(i);
            _keys.Remove(key);
        }
    }
}
