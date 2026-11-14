using FluentAssertions;
using FluentAssertions.Execution;

namespace GitSnapshotter.UnitTests;

public sealed class RestoreRemotesTests
{
    [Fact]
    public void RemovedRemoteIsAddedBackWithItsBranches()
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        var snapshot = clone.Snapshot();

        clone.Network.Remotes.Remove("origin");
        clone.RestoreFrom(snapshot);

        using var fresh = clone.Reopen();
        using (new AssertionScope())
        {
            fresh.Network.Remotes["origin"].Url.Should().Be(origin.Info.Path);
            fresh.Branches["origin/master"].Should().NotBeNull();
            fresh.Branches["master"].TrackedBranch.FriendlyName.Should().Be("origin/master");
            fresh.Snapshot().Should().BeEquivalentTo(snapshot);
        }
    }

    [Theory, AutoData]
    public void AddedRemoteIsRemoved(string remoteName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        using var other = GitTasks.CreateTemporaryGitRepository();
        other.CommitFile("b.txt", "1");
        var snapshot = repo.Snapshot();

        repo.AddRemote(remoteName, other.Info.Path);
        repo.Fetch(remoteName);
        repo.RestoreFrom(snapshot);

        using var fresh = repo.Reopen();
        using (new AssertionScope())
        {
            fresh.Network.Remotes[remoteName].Should().BeNull();
            fresh.Refs.Where(r => r.CanonicalName.StartsWith("refs/remotes/")).Should().BeEmpty();
        }
    }

    [Theory, AutoData]
    public void ChangedUrlIsRestored(string newUrl)
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        var snapshot = clone.Snapshot();

        clone.Network.Remotes.Update("origin", r => r.Url = newUrl);
        clone.RestoreFrom(snapshot);

        using var fresh = clone.Reopen();
        fresh.Network.Remotes["origin"].Url.Should().Be(origin.Info.Path);
    }

    [Fact]
    public void DeletedRemoteTrackingBranchIsRestored()
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        var snapshot = clone.Snapshot();

        clone.Refs.Remove("refs/remotes/origin/master");
        clone.RestoreFrom(snapshot);

        clone.Reopen().Snapshot().Should().BeEquivalentTo(snapshot);
    }

    [Theory, AutoData]
    public void FetchedRemoteTrackingBranchIsRemoved(string branchName)
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        var snapshot = clone.Snapshot();

        origin.AddBranch(branchName);
        clone.Fetch("origin");
        clone.RestoreFrom(snapshot);

        using var fresh = clone.Reopen();
        using (new AssertionScope())
        {
            fresh.Branches[$"origin/{branchName}"].Should().BeNull();
            fresh.Snapshot().Should().BeEquivalentTo(snapshot);
        }
    }

    [Fact]
    public void MovedRemoteTrackingBranchIsRestored()
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        var snapshot = clone.Snapshot();
        var originalTip = clone.Branches["origin/master"].Tip.Sha;

        origin.CommitFile("new.txt", "new");
        clone.Fetch("origin");
        clone.RestoreFrom(snapshot);

        using var fresh = clone.Reopen();
        fresh.Branches["origin/master"].Tip.Sha.Should().Be(originalTip);
    }

    [Fact]
    public void SnapshotRecordsRemoteHeadSeparately()
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;

        var remote = clone.Snapshot().Remotes.Single();

        using (new AssertionScope())
        {
            remote.Head.Should().Be("origin/master");
            remote.Branches.Keys.Should().Equal("origin/master");
        }
    }

    [Fact]
    public void RemovedRemoteHeadIsRestoredAsSymbolicReference()
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        var snapshot = clone.Snapshot();

        clone.Refs.Remove("refs/remotes/origin/HEAD");
        clone.RestoreFrom(snapshot);

        using var fresh = clone.Reopen();
        using (new AssertionScope())
        {
            fresh.Refs["refs/remotes/origin/HEAD"].Should().BeOfType<LibGit2Sharp.SymbolicReference>()
                .Which.TargetIdentifier.Should().Be("refs/remotes/origin/master");
            fresh.Snapshot().Should().BeEquivalentTo(snapshot);
        }
    }
}
