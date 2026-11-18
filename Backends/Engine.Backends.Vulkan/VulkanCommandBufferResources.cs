namespace Engine.Backends.Vulkan;

internal sealed class VulkanCommandBufferResources
{
    private readonly List<(object Key, IDisposable Lease)> _retained = [];
    private readonly List<Action> _pendingCommits = [];

    public bool HasRetainedResources => _retained.Count > 0;

    public bool Retain(object key, IDisposable lease)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(lease);
        if (_retained.Any(entry => ReferenceEquals(entry.Key, key)))
        {
            lease.Dispose();
            return false;
        }
        try
        {
            _retained.Add((key, lease));
            return true;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public void AddPendingCommit(Action commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        _pendingCommits.Add(commit);
    }

    public void CompleteSubmission(bool commitPendingBuilds)
    {
        try
        {
            if (commitPendingBuilds)
            {
                foreach (Action commit in _pendingCommits)
                    commit();
            }
        }
        finally
        {
            _pendingCommits.Clear();
            ReleaseRetained();
        }
    }

    public void DiscardUnsubmitted()
    {
        _pendingCommits.Clear();
        ReleaseRetained();
    }

    private void ReleaseRetained()
    {
        Exception? failure = null;
        for (int index = _retained.Count - 1; index >= 0; index--)
        {
            try
            {
                _retained[index].Lease.Dispose();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }
        _retained.Clear();
        if (failure is not null)
            throw failure;
    }
}
