using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

public sealed class RestoreHeadTests
{
    [Test, AutoData]
    public async Task RestoreHead(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.AddFileToRepository();
        repo.CommitChanges();
        repo.AddBranch(branchName);
        var originalSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);
        Commands.Checkout(repo, branchName);

        GitRepository.Restore(repo.Info.WorkingDirectory, originalSnapshot);
        var newSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        await SnapshotAssert.AreEquivalent(newSnapshot, originalSnapshot);
    }

    [Test]
    public async Task SnapshotOfDetachedHeadRecordsCommit()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");
        Commands.Checkout(repo, commit);

        var snapshot = repo.Snapshot();

        using (Assert.Multiple())
        {
            await Assert.That(snapshot.IsHeadDetached).IsTrue();
            await Assert.That(snapshot.Head).IsEqualTo(commit.Sha);
        }
    }

    [Test]
    public async Task SnapshotOfAttachedHeadIsNotDetached()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");

        await Assert.That(repo.Snapshot().IsHeadDetached).IsFalse();
    }

    [Test]
    public async Task RestoreDetachedHead()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var first = repo.CommitFile("a.txt", "1");
        repo.CommitFile("a.txt", "2");
        Commands.Checkout(repo, first);
        var snapshot = repo.Snapshot();

        Commands.Checkout(repo, "master");
        repo.RestoreFrom(snapshot);

        using var fresh = repo.Reopen();
        using (Assert.Multiple())
        {
            await Assert.That(fresh.Info.IsHeadDetached).IsTrue();
            await Assert.That(fresh.Head.Tip.Sha).IsEqualTo(first.Sha);
            await Assert.That(fresh.ReadFile("a.txt")).IsEqualTo("1");
            await SnapshotAssert.AreEquivalent(fresh.Snapshot(), snapshot);
        }
    }

    [Test]
    public async Task RestoreBranchFromDetachedHead()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var first = repo.CommitFile("a.txt", "1");
        repo.CommitFile("a.txt", "2");
        var snapshot = repo.Snapshot();

        Commands.Checkout(repo, first);
        repo.RestoreFrom(snapshot);

        using var fresh = repo.Reopen();
        using (Assert.Multiple())
        {
            await Assert.That(fresh.Info.IsHeadDetached).IsFalse();
            await Assert.That(fresh.Head.FriendlyName).IsEqualTo("master");
            await Assert.That(fresh.ReadFile("a.txt")).IsEqualTo("2");
        }
    }

    [Test, AutoData]
    public async Task RestoreWhileOnBranchCreatedAfterSnapshot(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();

        repo.AddBranch(branchName);
        repo.CheckoutBranch(branchName);
        repo.CommitFile("b.txt", "new");
        repo.RestoreFrom(snapshot);

        using var fresh = repo.Reopen();
        using (Assert.Multiple())
        {
            await Assert.That(fresh.Branches[branchName]).IsNull();
            await Assert.That(fresh.Head.FriendlyName).IsEqualTo("master");
            await Assert.That(fresh.FileExists("b.txt")).IsFalse();
            await Assert.That(fresh.IsClean()).IsTrue();
            await SnapshotAssert.AreEquivalent(fresh.Snapshot(), snapshot);
        }
    }

    [Test]
    public async Task RestoreMovesWorkingTreeWhenCheckedOutBranchTipMoves()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();

        repo.CommitFile("a.txt", "2");
        repo.CommitFile("b.txt", "added later");
        repo.RestoreFrom(snapshot);

        using var fresh = repo.Reopen();
        using (Assert.Multiple())
        {
            await Assert.That(fresh.ReadFile("a.txt")).IsEqualTo("1");
            await Assert.That(fresh.FileExists("b.txt")).IsFalse();
            await Assert.That(fresh.IsClean()).IsTrue();
        }
    }

    [Test]
    public async Task RestoreRefusesToOverwriteUncommittedChanges()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();
        var tip = repo.CommitFile("a.txt", "2");
        repo.WriteFile("a.txt", "uncommitted");

        var restore = () => repo.RestoreFrom(snapshot);

        using (Assert.Multiple())
        {
            var exception = await Assert.That(restore).Throws<UncommittedChangesException>();
            await Assert.That(exception!.Paths).IsEquivalentTo(["a.txt"], CollectionOrdering.Matching);
            using var fresh = repo.Reopen();
            await Assert.That(fresh.Head.Tip.Sha)
                .IsEqualTo(tip.Sha).Because("nothing should change when the restore is refused");
            await Assert.That(fresh.ReadFile("a.txt")).IsEqualTo("uncommitted");
        }
    }

    [Test]
    public async Task RestoreRefusesToOverwriteStagedChanges()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();
        repo.CommitFile("a.txt", "2");
        repo.WriteFile("a.txt", "staged");
        repo.StageFile("a.txt");

        var restore = () => repo.RestoreFrom(snapshot);

        await Assert.That(restore).Throws<UncommittedChangesException>();
    }

    [Test]
    public async Task RestoreWithForceDiscardsUncommittedChanges()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();
        repo.CommitFile("a.txt", "2");
        repo.WriteFile("a.txt", "uncommitted");

        repo.RestoreFrom(snapshot, new RestoreOptions { Force = true });

        using var fresh = repo.Reopen();
        using (Assert.Multiple())
        {
            await Assert.That(fresh.ReadFile("a.txt")).IsEqualTo("1");
            await Assert.That(fresh.IsClean()).IsTrue();
        }
    }

    [Test]
    public async Task UntrackedFilesSurviveRestoreWithoutWorkingTree()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();
        repo.CommitFile("a.txt", "2");
        repo.WriteFile("untracked.txt", "keep me");

        repo.RestoreFrom(snapshot);

        await Assert.That(repo.ReadFile("untracked.txt")).IsEqualTo("keep me");
    }

    [Test]
    public async Task UntrackedFileThatWouldBeOverwrittenBlocksRestore()
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

        var exception2 = await Assert.That(restore).Throws<UncommittedChangesException>();
        await Assert.That(exception2!.Paths).IsEquivalentTo(["b.txt"], CollectionOrdering.Matching);
    }

    [Test, AutoData]
    public async Task DirtyWorkingTreeDoesNotBlockRestoreThatKeepsHeadCommit(string tagName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();
        repo.AddTag(tagName);
        repo.WriteFile("a.txt", "uncommitted");

        repo.RestoreFrom(snapshot);

        using (Assert.Multiple())
        {
            await Assert.That(repo.Tags[tagName]).IsNull();
            await Assert.That(repo.ReadFile("a.txt")).IsEqualTo("uncommitted");
        }
    }

    [Test]
    public async Task RestoreEmptyRepository()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var snapshot = repo.Snapshot();

        repo.CommitFile("a.txt", "1");
        repo.RestoreFrom(snapshot);

        using var fresh = repo.Reopen();
        using (Assert.Multiple())
        {
            await Assert.That(fresh.Info.IsHeadUnborn).IsTrue();
            await Assert.That(fresh.Branches).IsEmpty();
            await Assert.That(fresh.FileExists("a.txt")).IsFalse();
            await SnapshotAssert.AreEquivalent(fresh.Snapshot(), snapshot);
        }
    }
}
