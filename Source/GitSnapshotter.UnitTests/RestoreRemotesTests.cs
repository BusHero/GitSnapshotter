namespace GitSnapshotter.UnitTests;

public sealed class RestoreRemotesTests
{
    [Test]
    public async Task RemovedRemoteIsAddedBackWithItsBranches()
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        var snapshot = clone.Snapshot();

        clone.Network.Remotes.Remove("origin");
        clone.RestoreFrom(snapshot);

        using var fresh = clone.Reopen();
        using (Assert.Multiple())
        {
            await Assert.That(fresh.Network.Remotes["origin"].Url).IsEqualTo(origin.Info.Path);
            await Assert.That(fresh.Branches["origin/master"]).IsNotNull();
            await Assert.That(fresh.Branches["master"].TrackedBranch.FriendlyName).IsEqualTo("origin/master");
            await SnapshotAssert.AreEquivalent(fresh.Snapshot(), snapshot);
        }
    }

    [Test, AutoData]
    public async Task AddedRemoteIsRemoved(string remoteName)
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
        using (Assert.Multiple())
        {
            await Assert.That(fresh.Network.Remotes[remoteName]).IsNull();
            await Assert.That(fresh.Refs.Where(r => r.CanonicalName.StartsWith("refs/remotes/", StringComparison.Ordinal))).IsEmpty();
        }
    }

    [Test, AutoData]
    public async Task ChangedUrlIsRestored(string newUrl)
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        var snapshot = clone.Snapshot();

        clone.Network.Remotes.Update("origin", r => r.Url = newUrl);
        clone.RestoreFrom(snapshot);

        using var fresh = clone.Reopen();
        await Assert.That(fresh.Network.Remotes["origin"].Url).IsEqualTo(origin.Info.Path);
    }

    [Test]
    public async Task DeletedRemoteTrackingBranchIsRestored()
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        var snapshot = clone.Snapshot();

        clone.Refs.Remove("refs/remotes/origin/master");
        clone.RestoreFrom(snapshot);

        using var reopened = clone.Reopen();
        await SnapshotAssert.AreEquivalent(reopened.Snapshot(), snapshot);
    }

    [Test, AutoData]
    public async Task FetchedRemoteTrackingBranchIsRemoved(string branchName)
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        var snapshot = clone.Snapshot();

        origin.AddBranch(branchName);
        clone.Fetch("origin");
        clone.RestoreFrom(snapshot);

        using var fresh = clone.Reopen();
        using (Assert.Multiple())
        {
            await Assert.That(fresh.Branches[$"origin/{branchName}"]).IsNull();
            await SnapshotAssert.AreEquivalent(fresh.Snapshot(), snapshot);
        }
    }

    [Test]
    public async Task MovedRemoteTrackingBranchIsRestored()
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
        await Assert.That(fresh.Branches["origin/master"].Tip.Sha).IsEqualTo(originalTip);
    }

    [Test]
    public async Task SnapshotRecordsRemoteHeadSeparately()
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;

        var remote = clone.Snapshot().Remotes.Single();

        using (Assert.Multiple())
        {
            await Assert.That(remote.Head).IsEqualTo("origin/master");
            await Assert.That(remote.Branches.Keys).IsEquivalentTo(["origin/master"], CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task RemovedRemoteHeadIsRestoredAsSymbolicReference()
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        var snapshot = clone.Snapshot();

        clone.Refs.Remove("refs/remotes/origin/HEAD");
        clone.RestoreFrom(snapshot);

        using var fresh = clone.Reopen();
        using (Assert.Multiple())
        {
            var typed = await Assert.That(fresh.Refs["refs/remotes/origin/HEAD"])
                .IsTypeOf<LibGit2Sharp.SymbolicReference>();
            await Assert.That(typed!.TargetIdentifier).IsEqualTo("refs/remotes/origin/master");
            await SnapshotAssert.AreEquivalent(fresh.Snapshot(), snapshot);
        }
    }
}
