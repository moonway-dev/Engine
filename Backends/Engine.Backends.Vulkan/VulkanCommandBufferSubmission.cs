namespace Engine.Backends.Vulkan;

internal enum VulkanCommandSubmissionState
{
    Recording,
    Executable,
    Submitted,
    Completed,
    Cancelled,
    Abandoned,
}

internal sealed class VulkanCommandBufferSubmission
{
    private readonly VulkanCommandBufferResources _resources = new();
    private bool _submissionAccepted;

    public VulkanCommandSubmissionState State { get; private set; } = VulkanCommandSubmissionState.Recording;

    public bool HasRetainedResources => _resources.HasRetainedResources;

    public bool SubmissionAccepted => _submissionAccepted;

    public void Retain(object key, IDisposable lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (State != VulkanCommandSubmissionState.Recording)
        {
            lease.Dispose();
            EnsureRecordable();
        }
        _resources.Retain(key, lease);
    }

    public void AddPendingCommit(Action commit)
    {
        EnsureRecordable();
        _resources.AddPendingCommit(commit);
    }

    public void MarkExecutable()
    {
        if (State != VulkanCommandSubmissionState.Recording)
            throw new InvalidOperationException($"Cannot end command recording in state {State}.");
        State = VulkanCommandSubmissionState.Executable;
    }

    public void MarkSubmitted()
    {
        if (State != VulkanCommandSubmissionState.Executable)
            throw new InvalidOperationException($"Cannot submit a command buffer in state {State}.");
        _submissionAccepted = false;
        State = VulkanCommandSubmissionState.Submitted;
    }

    public void MarkQueueAccepted()
    {
        if (State != VulkanCommandSubmissionState.Submitted || _submissionAccepted)
            throw new InvalidOperationException($"Cannot confirm queue acceptance in state {State}.");
        _submissionAccepted = true;
    }

    public void ResetForRecording()
    {
        if (State is VulkanCommandSubmissionState.Submitted or VulkanCommandSubmissionState.Abandoned)
            throw new InvalidOperationException($"Cannot reset a command buffer in state {State} before completion.");
        if (State is VulkanCommandSubmissionState.Recording or VulkanCommandSubmissionState.Executable)
            _resources.DiscardUnsubmitted();
        _submissionAccepted = false;
        State = VulkanCommandSubmissionState.Recording;
    }

    public void RollbackSubmit()
    {
        if (State != VulkanCommandSubmissionState.Submitted)
            return;
        if (_submissionAccepted)
            throw new InvalidOperationException("An accepted queue submission cannot be rolled back.");
        State = VulkanCommandSubmissionState.Executable;
    }

    public void CancelSubmission()
    {
        if (State is VulkanCommandSubmissionState.Submitted or VulkanCommandSubmissionState.Abandoned)
            throw new InvalidOperationException("A submitted or abandoned command buffer cannot be cancelled before confirmed idle.");
        _resources.DiscardUnsubmitted();
        _submissionAccepted = false;
        State = VulkanCommandSubmissionState.Cancelled;
    }

    public void CompleteSubmission()
    {
        if (State is not (VulkanCommandSubmissionState.Submitted or VulkanCommandSubmissionState.Abandoned))
            return;
        bool commitPendingBuilds = _submissionAccepted;
        try
        {
            _resources.CompleteSubmission(commitPendingBuilds);
        }
        finally
        {
            State = VulkanCommandSubmissionState.Completed;
        }
    }

    public void AbandonSubmission()
    {
        if (State == VulkanCommandSubmissionState.Submitted)
            State = VulkanCommandSubmissionState.Abandoned;
    }

    private void EnsureRecordable()
    {
        if (State != VulkanCommandSubmissionState.Recording)
            throw new InvalidOperationException($"Cannot add resources in command state {State}.");
    }
}
