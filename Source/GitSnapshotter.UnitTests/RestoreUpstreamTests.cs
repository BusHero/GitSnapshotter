using FluentAssertions;
using FluentAssertions.Execution;

namespace GitSnapshotter.UnitTests;

public sealed class RestoreUpstreamTests
{
    [Fact]
    public void SnapshotRecordsUpstreamBranch()
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;

        var master = clone.Snapshot().Branches.Single(b => b.Name == "master");

        using (new AssertionScope())
        {
            master.RemoteName.Should().Be("origin");
            master.UpstreamBranch.Should().Be("refs/heads/master");
            master.TrackedBranch.Should().Be("origin/master");
        }
    }

    [Fact]
    public void RemovedUpstreamIsRestored()
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        var snapshot = clone.Snapshot();

        clone.Branches.Update(clone.Branches["master"], b => b.TrackedBranch = null);
        clone.RestoreFrom(snapshot);

        using var fresh = clone.Reopen();
        using (new AssertionScope())
        {
            fresh.Branches["master"].TrackedBranch.FriendlyName.Should().Be("origin/master");
            fresh.Snapshot().Should().BeEquivalentTo(snapshot);
        }
    }

    [Theory, AutoData]
    public void AddedUpstreamIsRemoved(string branchName)
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        clone.AddBranch(branchName);
        var snapshot = clone.Snapshot();

        clone.Branches.Update(clone.Branches[branchName],
            b => b.Remote = "origin",
            b => b.UpstreamBranch = "refs/heads/master");
        clone.RestoreFrom(snapshot);

        using var fresh = clone.Reopen();
        using (new AssertionScope())
        {
            fresh.Branches[branchName].IsTracking.Should().BeFalse();
            fresh.Config.Get<string>($"branch.{branchName}.remote").Should().BeNull();
            fresh.Config.Get<string>($"branch.{branchName}.merge").Should().BeNull();
        }
    }

    [Theory, AutoData]
    public void ChangedUpstreamIsRestored(string otherBranch)
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        origin.AddBranch(otherBranch);
        clone.Fetch("origin");
        var snapshot = clone.Snapshot();

        clone.Branches.Update(clone.Branches["master"], b => b.UpstreamBranch = $"refs/heads/{otherBranch}");
        clone.RestoreFrom(snapshot);

        using var fresh = clone.Reopen();
        fresh.Branches["master"].UpstreamBranchCanonicalName.Should().Be("refs/heads/master");
    }

    [Fact]
    public void UpstreamIsRestoredFromSnapshotWithoutUpstreamBranch()
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        // Snapshots from earlier versions only had TrackedBranch and RemoteName.
        var snapshot = clone.Snapshot();
        snapshot = snapshot with
        {
            Branches = snapshot.Branches.Select(b => b with { UpstreamBranch = null }).ToArray(),
        };

        clone.Branches.Update(clone.Branches["master"], b => b.TrackedBranch = null);
        clone.RestoreFrom(snapshot);

        using var fresh = clone.Reopen();
        fresh.Branches["master"].UpstreamBranchCanonicalName.Should().Be("refs/heads/master");
    }
}
