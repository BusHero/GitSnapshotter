using FluentAssertions;
using FluentAssertions.Execution;

using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

public sealed class RestoreHeadTests
{
    [Test, AutoData]
    public void RestoreHead(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.AddFileToRepository();
        repo.CommitChanges();
        repo.AddBranch(branchName);
        var originalSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);
        Commands.Checkout(repo, branchName);

        GitRepository.Restore(repo.Info.WorkingDirectory, originalSnapshot);
        var newSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        newSnapshot.Should().BeEquivalentTo(originalSnapshot);
    }

    [Test]
    public void SnapshotOfDetachedHeadRecordsCommit()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");
        Commands.Checkout(repo, commit);

        var snapshot = repo.Snapshot();

        using (new AssertionScope())
        {
            snapshot.IsHeadDetached.Should().BeTrue();
            snapshot.Head.Should().Be(commit.Sha);
        }
    }

    [Test]
    public void SnapshotOfAttachedHeadIsNotDetached()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");

        repo.Snapshot().IsHeadDetached.Should().BeFalse();
    }

    [Test]
    public void RestoreDetachedHead()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var first = repo.CommitFile("a.txt", "1");
        repo.CommitFile("a.txt", "2");
        Commands.Checkout(repo, first);
        var snapshot = repo.Snapshot();

        Commands.Checkout(repo, "master");
        repo.RestoreFrom(snapshot);

        using var fresh = repo.Reopen();
        using (new AssertionScope())
        {
            fresh.Info.IsHeadDetached.Should().BeTrue();
            fresh.Head.Tip.Sha.Should().Be(first.Sha);
            fresh.ReadFile("a.txt").Should().Be("1");
            fresh.Snapshot().Should().BeEquivalentTo(snapshot);
        }
    }

    [Test]
    public void RestoreBranchFromDetachedHead()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var first = repo.CommitFile("a.txt", "1");
        repo.CommitFile("a.txt", "2");
        var snapshot = repo.Snapshot();

        Commands.Checkout(repo, first);
        repo.RestoreFrom(snapshot);

        using var fresh = repo.Reopen();
        using (new AssertionScope())
        {
            fresh.Info.IsHeadDetached.Should().BeFalse();
            fresh.Head.FriendlyName.Should().Be("master");
            fresh.ReadFile("a.txt").Should().Be("2");
        }
    }

    [Test, AutoData]
    public void RestoreWhileOnBranchCreatedAfterSnapshot(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();

        repo.AddBranch(branchName);
        repo.CheckoutBranch(branchName);
        repo.CommitFile("b.txt", "new");
        repo.RestoreFrom(snapshot);

        using var fresh = repo.Reopen();
        using (new AssertionScope())
        {
            fresh.Branches[branchName].Should().BeNull();
            fresh.Head.FriendlyName.Should().Be("master");
            fresh.FileExists("b.txt").Should().BeFalse();
            fresh.IsClean().Should().BeTrue();
            fresh.Snapshot().Should().BeEquivalentTo(snapshot);
        }
    }

    [Test]
    public void RestoreMovesWorkingTreeWhenCheckedOutBranchTipMoves()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();

        repo.CommitFile("a.txt", "2");
        repo.CommitFile("b.txt", "added later");
        repo.RestoreFrom(snapshot);

        using var fresh = repo.Reopen();
        using (new AssertionScope())
        {
            fresh.ReadFile("a.txt").Should().Be("1");
            fresh.FileExists("b.txt").Should().BeFalse();
            fresh.IsClean().Should().BeTrue();
        }
    }

    [Test]
    public void RestoreRefusesToOverwriteUncommittedChanges()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();
        var tip = repo.CommitFile("a.txt", "2");
        repo.WriteFile("a.txt", "uncommitted");

        var restore = () => repo.RestoreFrom(snapshot);

        using (new AssertionScope())
        {
            restore.Should().Throw<UncommittedChangesException>()
                .Which.Paths.Should().Equal("a.txt");
            using var fresh = repo.Reopen();
            fresh.Head.Tip.Sha.Should().Be(tip.Sha, "nothing should change when the restore is refused");
            fresh.ReadFile("a.txt").Should().Be("uncommitted");
        }
    }

    [Test]
    public void RestoreRefusesToOverwriteStagedChanges()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();
        repo.CommitFile("a.txt", "2");
        repo.WriteFile("a.txt", "staged");
        repo.StageFile("a.txt");

        var restore = () => repo.RestoreFrom(snapshot);

        restore.Should().Throw<UncommittedChangesException>();
    }

    [Test]
    public void RestoreWithForceDiscardsUncommittedChanges()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();
        repo.CommitFile("a.txt", "2");
        repo.WriteFile("a.txt", "uncommitted");

        repo.RestoreFrom(snapshot, new RestoreOptions { Force = true });

        using var fresh = repo.Reopen();
        using (new AssertionScope())
        {
            fresh.ReadFile("a.txt").Should().Be("1");
            fresh.IsClean().Should().BeTrue();
        }
    }

    [Test]
    public void UntrackedFilesSurviveRestoreWithoutWorkingTree()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();
        repo.CommitFile("a.txt", "2");
        repo.WriteFile("untracked.txt", "keep me");

        repo.RestoreFrom(snapshot);

        repo.ReadFile("untracked.txt").Should().Be("keep me");
    }

    [Test]
    public void UntrackedFileThatWouldBeOverwrittenBlocksRestore()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.CommitFile("b.txt", "tracked in the snapshot");
        var snapshot = repo.Snapshot();
        repo.Index.Remove("b.txt");
        repo.Index.Write();
        repo.CommitChanges();
        repo.WriteFile("b.txt", "now untracked and different");

        var restore = () => repo.RestoreFrom(snapshot);

        restore.Should().Throw<UncommittedChangesException>().Which.Paths.Should().Equal("b.txt");
    }

    [Test, AutoData]
    public void DirtyWorkingTreeDoesNotBlockRestoreThatKeepsHeadCommit(string tagName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();
        repo.AddTag(tagName);
        repo.WriteFile("a.txt", "uncommitted");

        repo.RestoreFrom(snapshot);

        using (new AssertionScope())
        {
            repo.Tags[tagName].Should().BeNull();
            repo.ReadFile("a.txt").Should().Be("uncommitted");
        }
    }

    [Test]
    public void RestoreEmptyRepository()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var snapshot = repo.Snapshot();

        repo.CommitFile("a.txt", "1");
        repo.RestoreFrom(snapshot);

        using var fresh = repo.Reopen();
        using (new AssertionScope())
        {
            fresh.Info.IsHeadUnborn.Should().BeTrue();
            fresh.Branches.Should().BeEmpty();
            fresh.FileExists("a.txt").Should().BeFalse();
            fresh.Snapshot().Should().BeEquivalentTo(snapshot);
        }
    }
}
