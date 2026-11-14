namespace GitSnapshotter.UnitTests;

public sealed class SnapshotDiffTests
{
    [Test]
    public async Task IdenticalSnapshotsHaveNoChanges()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");

        var diff = SnapshotDiff.Compare(repo.Snapshot(), repo.Snapshot());

        using (Assert.Multiple())
        {
            await Assert.That(diff.HasChanges).IsFalse();
            await Assert.That(diff.Changes).IsEmpty();
        }
    }

    [Test, AutoData]
    public async Task AddedBranchIsReported(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");
        var before = repo.Snapshot();
        repo.AddBranch(branchName);

        var diff = SnapshotDiff.Compare(before, repo.Snapshot());

        await Assert.That(diff.Changes)
            .IsEquivalentTo(
                [
                    new SnapshotChange(SnapshotChangeKind.Added, ChangeCategory.Branch, branchName, null, commit.Sha),
                ],
                CollectionOrdering.Matching);
    }

    [Test, AutoData]
    public async Task RemovedBranchIsReported(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");
        repo.AddBranch(branchName);
        var before = repo.Snapshot();
        repo.Branches.Remove(branchName);

        var diff = SnapshotDiff.Compare(before, repo.Snapshot());

        await Assert.That(diff.Changes)
            .IsEquivalentTo(
                [
                    new SnapshotChange(SnapshotChangeKind.Removed, ChangeCategory.Branch, branchName, commit.Sha, null),
                ],
                CollectionOrdering.Matching);
    }

    [Test]
    public async Task MovedBranchIsReported()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var first = repo.CommitFile("a.txt", "1");
        var before = repo.Snapshot();
        var second = repo.CommitFile("a.txt", "2");

        var diff = SnapshotDiff.Compare(before, repo.Snapshot());

        await Assert.That(diff.Changes)
            .IsEquivalentTo(
                [
                    new SnapshotChange(SnapshotChangeKind.Modified, ChangeCategory.Branch, "master", first.Sha, second.Sha),
                ],
                CollectionOrdering.Matching);
    }

    [Test, AutoData]
    public async Task HeadChangeIsReported(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.AddBranch(branchName);
        var before = repo.Snapshot();
        repo.CheckoutBranch(branchName);

        var diff = SnapshotDiff.Compare(before, repo.Snapshot());

        await Assert.That(diff.Changes)
            .IsEquivalentTo(
                [
                    new SnapshotChange(SnapshotChangeKind.Modified, ChangeCategory.Head, "HEAD", "master", branchName),
                ],
                CollectionOrdering.Matching);
    }

    [Test, AutoData]
    public async Task TagChangesAreReported(string added, string removed)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");
        repo.AddTag(removed);
        var before = repo.Snapshot();
        repo.Tags.Remove(removed);
        repo.AddTag(added);

        var diff = SnapshotDiff.Compare(before, repo.Snapshot());

        await Assert.That(diff.Changes)
            .IsEquivalentTo(
                [
                    new SnapshotChange(SnapshotChangeKind.Removed, ChangeCategory.Tag, removed, commit.Sha, null),
                    new SnapshotChange(SnapshotChangeKind.Added, ChangeCategory.Tag, added, null, commit.Sha),
                ]);
    }

    [Test, AutoData]
    public async Task RemoteAndUpstreamChangesAreReported(string newUrl)
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        var before = clone.Snapshot();
        clone.Network.Remotes.Update("origin", r => r.Url = newUrl);
        clone.Branches.Update(clone.Branches["master"], b => b.TrackedBranch = null);

        var diff = SnapshotDiff.Compare(before, clone.Snapshot());

        await Assert.That(diff.Changes)
            .IsEquivalentTo(
                [
                    new SnapshotChange(SnapshotChangeKind.Removed, ChangeCategory.Upstream, "master", "origin refs/heads/master", null),
                    new SnapshotChange(SnapshotChangeKind.Modified, ChangeCategory.Remote, "origin", origin.Info.Path, newUrl),
                ]);
    }

    [Test]
    public async Task RemoteBranchChangesAreReported()
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        var before = clone.Snapshot();
        clone.Refs.Remove("refs/remotes/origin/HEAD");

        var diff = SnapshotDiff.Compare(before, clone.Snapshot());

        await Assert.That(diff.Changes)
            .IsEquivalentTo(
                [
                    new SnapshotChange(SnapshotChangeKind.Removed, ChangeCategory.RemoteBranch, "origin/HEAD", "-> origin/master", null),
                ],
                CollectionOrdering.Matching);
    }

    [Test]
    public async Task ReferenceChangesAreReported()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");
        var before = repo.Snapshot();
        repo.Refs.Add("refs/custom/marker", commit.Id);

        var diff = SnapshotDiff.Compare(before, repo.Snapshot());

        await Assert.That(diff.Changes)
            .IsEquivalentTo(
                [
                    new SnapshotChange(SnapshotChangeKind.Added, ChangeCategory.Reference, "refs/custom/marker", null, commit.Sha),
                ],
                CollectionOrdering.Matching);
    }

    [Test]
    public async Task WorkingTreeChangesAreReportedOnlyWhenBothSnapshotsHaveIt()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var before = repo.SnapshotWithWorkingTree();
        repo.WriteFile("a.txt", "changed");
        var after = repo.SnapshotWithWorkingTree();

        using (Assert.Multiple())
        {
            var change = await Assert.That(SnapshotDiff.Compare(before, after).Changes).HasSingleItem();
            await Assert.That(change.Category).IsEqualTo(ChangeCategory.WorkingTree);
            await Assert.That(change.Name).IsEqualTo("working directory");
            await Assert.That(SnapshotDiff.Compare(before, repo.Snapshot()).HasChanges).IsFalse();
        }
    }

    [Test]
    public async Task ChangesHaveReadableText()
    {
        var change = new SnapshotChange(SnapshotChangeKind.Modified, ChangeCategory.Branch, "main", "aaa", "bbb");

        await Assert.That(change.ToString()).IsEqualTo("~ Branch main aaa -> bbb");
    }
}
