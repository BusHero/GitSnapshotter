using FluentAssertions;
using FluentAssertions.Execution;

namespace GitSnapshotter.UnitTests;

public sealed class SnapshotDiffTests
{
    [Test]
    public void IdenticalSnapshotsHaveNoChanges()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");

        var diff = SnapshotDiff.Compare(repo.Snapshot(), repo.Snapshot());

        using (new AssertionScope())
        {
            diff.HasChanges.Should().BeFalse();
            diff.Changes.Should().BeEmpty();
        }
    }

    [Test, AutoData]
    public void AddedBranchIsReported(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");
        var before = repo.Snapshot();
        repo.AddBranch(branchName);

        var diff = SnapshotDiff.Compare(before, repo.Snapshot());

        diff.Changes.Should().Equal(
            new SnapshotChange(SnapshotChangeKind.Added, ChangeCategory.Branch, branchName, null, commit.Sha));
    }

    [Test, AutoData]
    public void RemovedBranchIsReported(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");
        repo.AddBranch(branchName);
        var before = repo.Snapshot();
        repo.Branches.Remove(branchName);

        var diff = SnapshotDiff.Compare(before, repo.Snapshot());

        diff.Changes.Should().Equal(
            new SnapshotChange(SnapshotChangeKind.Removed, ChangeCategory.Branch, branchName, commit.Sha, null));
    }

    [Test]
    public void MovedBranchIsReported()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var first = repo.CommitFile("a.txt", "1");
        var before = repo.Snapshot();
        var second = repo.CommitFile("a.txt", "2");

        var diff = SnapshotDiff.Compare(before, repo.Snapshot());

        diff.Changes.Should().Equal(
            new SnapshotChange(SnapshotChangeKind.Modified, ChangeCategory.Branch, "master", first.Sha, second.Sha));
    }

    [Test, AutoData]
    public void HeadChangeIsReported(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.AddBranch(branchName);
        var before = repo.Snapshot();
        repo.CheckoutBranch(branchName);

        var diff = SnapshotDiff.Compare(before, repo.Snapshot());

        diff.Changes.Should().Equal(
            new SnapshotChange(SnapshotChangeKind.Modified, ChangeCategory.Head, "HEAD", "master", branchName));
    }

    [Test, AutoData]
    public void TagChangesAreReported(string added, string removed)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");
        repo.AddTag(removed);
        var before = repo.Snapshot();
        repo.Tags.Remove(removed);
        repo.AddTag(added);

        var diff = SnapshotDiff.Compare(before, repo.Snapshot());

        diff.Changes.Should().BeEquivalentTo([
            new SnapshotChange(SnapshotChangeKind.Removed, ChangeCategory.Tag, removed, commit.Sha, null),
            new SnapshotChange(SnapshotChangeKind.Added, ChangeCategory.Tag, added, null, commit.Sha),
        ]);
    }

    [Test, AutoData]
    public void RemoteAndUpstreamChangesAreReported(string newUrl)
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        var before = clone.Snapshot();
        clone.Network.Remotes.Update("origin", r => r.Url = newUrl);
        clone.Branches.Update(clone.Branches["master"], b => b.TrackedBranch = null);

        var diff = SnapshotDiff.Compare(before, clone.Snapshot());

        diff.Changes.Should().BeEquivalentTo([
            new SnapshotChange(SnapshotChangeKind.Removed, ChangeCategory.Upstream, "master", "origin refs/heads/master", null),
            new SnapshotChange(SnapshotChangeKind.Modified, ChangeCategory.Remote, "origin", origin.Info.Path, newUrl),
        ]);
    }

    [Test]
    public void RemoteBranchChangesAreReported()
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        var before = clone.Snapshot();
        clone.Refs.Remove("refs/remotes/origin/HEAD");

        var diff = SnapshotDiff.Compare(before, clone.Snapshot());

        diff.Changes.Should().Equal(
            new SnapshotChange(SnapshotChangeKind.Removed, ChangeCategory.RemoteBranch, "origin/HEAD", "-> origin/master", null));
    }

    [Test]
    public void ReferenceChangesAreReported()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");
        var before = repo.Snapshot();
        repo.Refs.Add("refs/custom/marker", commit.Id);

        var diff = SnapshotDiff.Compare(before, repo.Snapshot());

        diff.Changes.Should().Equal(
            new SnapshotChange(SnapshotChangeKind.Added, ChangeCategory.Reference, "refs/custom/marker", null, commit.Sha));
    }

    [Test]
    public void WorkingTreeChangesAreReportedOnlyWhenBothSnapshotsHaveIt()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var before = repo.SnapshotWithWorkingTree();
        repo.WriteFile("a.txt", "changed");
        var after = repo.SnapshotWithWorkingTree();

        using (new AssertionScope())
        {
            SnapshotDiff.Compare(before, after).Changes.Should().ContainSingle()
                .Which.Should().Match<SnapshotChange>(c =>
                    c.Category == ChangeCategory.WorkingTree && c.Name == "working directory");
            SnapshotDiff.Compare(before, repo.Snapshot()).HasChanges.Should().BeFalse();
        }
    }

    [Test]
    public void ChangesHaveReadableText()
    {
        var change = new SnapshotChange(SnapshotChangeKind.Modified, ChangeCategory.Branch, "main", "aaa", "bbb");

        change.ToString().Should().Be("~ Branch main aaa -> bbb");
    }
}
