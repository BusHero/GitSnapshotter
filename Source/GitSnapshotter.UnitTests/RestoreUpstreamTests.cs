namespace GitSnapshotter.UnitTests;

public sealed class RestoreUpstreamTests
{
    [Test]
    public async Task SnapshotRecordsUpstreamBranch()
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;

        var master = clone.Snapshot().Branches.Single(b => b.Name == "master");

        using (Assert.Multiple())
        {
            await Assert.That(master.RemoteName).IsEqualTo("origin");
            await Assert.That(master.UpstreamBranch).IsEqualTo("refs/heads/master");
            await Assert.That(master.TrackedBranch).IsEqualTo("origin/master");
        }
    }

    [Test]
    public async Task RemovedUpstreamIsRestored()
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        var snapshot = clone.Snapshot();

        clone.Branches.Update(clone.Branches["master"], b => b.TrackedBranch = null);
        clone.RestoreFrom(snapshot);

        using var fresh = clone.Reopen();
        using (Assert.Multiple())
        {
            await Assert.That(fresh.Branches["master"].TrackedBranch.FriendlyName).IsEqualTo("origin/master");
            await Assert.That(fresh.Snapshot()).IsEquivalentTo(snapshot);
        }
    }

    [Test, AutoData]
    public async Task AddedUpstreamIsRemoved(string branchName)
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
        using (Assert.Multiple())
        {
            await Assert.That(fresh.Branches[branchName].IsTracking).IsFalse();
            await Assert.That(fresh.Config.Get<string>($"branch.{branchName}.remote")).IsNull();
            await Assert.That(fresh.Config.Get<string>($"branch.{branchName}.merge")).IsNull();
        }
    }

    [Test, AutoData]
    public async Task ChangedUpstreamIsRestored(string otherBranch)
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
        await Assert.That(fresh.Branches["master"].UpstreamBranchCanonicalName).IsEqualTo("refs/heads/master");
    }

    [Test]
    public async Task UpstreamIsRestoredFromSnapshotWithoutUpstreamBranch()
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
        await Assert.That(fresh.Branches["master"].UpstreamBranchCanonicalName).IsEqualTo("refs/heads/master");
    }
}
