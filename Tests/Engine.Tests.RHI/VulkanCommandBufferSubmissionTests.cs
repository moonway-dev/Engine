using Engine.Backends.Vulkan;
using Xunit;

namespace Engine.Tests.RHI;

public class VulkanCommandBufferSubmissionTests
{
    [Fact]
    public void CancelBeforeSubmit_DropsPendingBuildAndReleasesRetainedResource()
    {
        VulkanCommandBufferSubmission submission = new();
        int disposed = 0;
        int committed = 0;
        submission.Retain(new object(), new TestLease(() => disposed++));
        submission.AddPendingCommit(() => committed++);
        submission.MarkExecutable();

        submission.CancelSubmission();

        Assert.Equal(VulkanCommandSubmissionState.Cancelled, submission.State);
        Assert.Equal(0, committed);
        Assert.Equal(1, disposed);
        Assert.False(submission.HasRetainedResources);
    }

    [Fact]
    public void CompleteSubmission_CommitsOnlyAfterQueueAcceptance()
    {
        VulkanCommandBufferSubmission submission = new();
        int disposed = 0;
        int committed = 0;
        submission.Retain(new object(), new TestLease(() => disposed++));
        submission.AddPendingCommit(() => committed++);
        submission.MarkExecutable();
        submission.MarkSubmitted();
        submission.MarkQueueAccepted();

        submission.CompleteSubmission();

        Assert.Equal(VulkanCommandSubmissionState.Completed, submission.State);
        Assert.Equal(1, committed);
        Assert.Equal(1, disposed);
        Assert.False(submission.HasRetainedResources);
    }

    [Fact]
    public void FailedSubmitRollbackAndCancel_DoesNotCommitPendingBuild()
    {
        VulkanCommandBufferSubmission submission = new();
        int disposed = 0;
        int committed = 0;
        submission.Retain(new object(), new TestLease(() => disposed++));
        submission.AddPendingCommit(() => committed++);
        submission.MarkExecutable();
        submission.MarkSubmitted();

        submission.RollbackSubmit();
        submission.CancelSubmission();

        Assert.Equal(VulkanCommandSubmissionState.Cancelled, submission.State);
        Assert.Equal(0, committed);
        Assert.Equal(1, disposed);
    }

    [Fact]
    public void AbandonedSubmission_RetainsResourcesUntilConfirmedCompletion()
    {
        VulkanCommandBufferSubmission submission = new();
        int disposed = 0;
        int committed = 0;
        submission.Retain(new object(), new TestLease(() => disposed++));
        submission.AddPendingCommit(() => committed++);
        submission.MarkExecutable();
        submission.MarkSubmitted();
        submission.MarkQueueAccepted();
        submission.AbandonSubmission();

        Assert.Equal(VulkanCommandSubmissionState.Abandoned, submission.State);
        Assert.True(submission.HasRetainedResources);
        Assert.Equal(0, disposed);
        Assert.Equal(0, committed);

        submission.CompleteSubmission();

        Assert.Equal(VulkanCommandSubmissionState.Completed, submission.State);
        Assert.Equal(1, disposed);
        Assert.Equal(1, committed);
    }

    [Fact]
    public void CompletionFailure_StillReleasesRetainedResourcesAndClosesState()
    {
        VulkanCommandBufferSubmission submission = new();
        int disposed = 0;
        submission.Retain(new object(), new TestLease(() => disposed++));
        submission.AddPendingCommit(() => throw new InvalidOperationException("commit failed"));
        submission.MarkExecutable();
        submission.MarkSubmitted();
        submission.MarkQueueAccepted();

        Assert.Throws<InvalidOperationException>(submission.CompleteSubmission);

        Assert.Equal(VulkanCommandSubmissionState.Completed, submission.State);
        Assert.Equal(1, disposed);
        Assert.False(submission.HasRetainedResources);
    }

    [Fact]
    public void DuplicateRetainKey_ReleasesDuplicateLeaseOnly()
    {
        VulkanCommandBufferResources resources = new();
        object key = new();
        int firstDisposed = 0;
        int duplicateDisposed = 0;

        Assert.True(resources.Retain(key, new TestLease(() => firstDisposed++)));
        Assert.False(resources.Retain(key, new TestLease(() => duplicateDisposed++)));

        resources.DiscardUnsubmitted();

        Assert.Equal(1, firstDisposed);
        Assert.Equal(1, duplicateDisposed);
        Assert.False(resources.HasRetainedResources);
    }

    [Fact]
    public void Quarantine_DrainsOnlyAfterExplicitConfirmedIdleAndPreservesEntriesOnReleaseFailure()
    {
        VulkanQuarantine<int, string> quarantine = new();
        List<string> released = [];

        Assert.True(quarantine.TryAdd(1, "first"));
        Assert.False(quarantine.TryAdd(1, "duplicate"));
        Assert.True(quarantine.TryAdd(2, "second"));
        Assert.Equal(2, quarantine.Count);

        Assert.Throws<InvalidOperationException>(() => quarantine.DrainAfterConfirmedIdle(deviceIdleConfirmed: false, released.Add));
        Assert.Equal(2, quarantine.Count);

        Assert.Throws<InvalidOperationException>(() => quarantine.DrainAfterConfirmedIdle(deviceIdleConfirmed: true, release: resource =>
        {
            if (resource == "second")
                throw new InvalidOperationException("release failed");
            released.Add(resource);
        }));

        Assert.Equal(new[] { "first" }, released);
        Assert.Equal(1, quarantine.Count);
        quarantine.DrainAfterConfirmedIdle(deviceIdleConfirmed: true, released.Add);
        Assert.Equal(new[] { "first", "second" }, released);
        Assert.Equal(0, quarantine.Count);
    }

    private sealed class TestLease(Action onDispose) : IDisposable
    {
        private Action? _onDispose = onDispose;

        public void Dispose() => Interlocked.Exchange(ref _onDispose, null)?.Invoke();
    }
}
