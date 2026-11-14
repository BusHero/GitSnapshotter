using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

public sealed class WorkingTreeTests
{
    [Test]
    public async Task WorkingTreeIsNotCapturedByDefault()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");

        await Assert.That(repo.Snapshot().WorkingTree).IsNull();
    }

    [Test]
    public async Task CleanWorkingTreeMatchesHeadTree()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");

        var state = repo.SnapshotWithWorkingTree().WorkingTree!;

        using (Assert.Multiple())
        {
            await Assert.That(state.IndexTree).IsEqualTo(commit.Tree.Sha);
            await Assert.That(state.WorkingDirectoryTree).IsEqualTo(commit.Tree.Sha);
        }
    }

    [Test]
    public async Task CapturingDoesNotChangeTheWorkingTree()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.WriteFile("a.txt", "modified");
        repo.WriteFile("untracked.txt", "untracked");
        repo.WriteFile("staged.txt", "staged");
        repo.StageFile("staged.txt");
        var statusBefore = repo.RetrieveStatus().Select(e => (e.FilePath, e.State)).ToArray();
        var refsBefore = repo.Refs.Select(r => (r.CanonicalName, r.TargetIdentifier)).ToArray();

        repo.SnapshotWithWorkingTree();

        using var fresh = repo.Reopen();
        using (Assert.Multiple())
        {
            await Assert.That(fresh.RetrieveStatus().Select(e => (e.FilePath, e.State))).IsEquivalentTo(statusBefore);
            await Assert.That(fresh.Refs.Select(r => (r.CanonicalName, r.TargetIdentifier))).IsEquivalentTo(refsBefore);
        }
    }

    [Test]
    public async Task ModifiedFileIsRestored()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.WriteFile("a.txt", "work in progress");
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.WriteFile("a.txt", "something else");
        repo.RestoreFrom(snapshot, new RestoreOptions { Force = true });

        using var fresh = repo.Reopen();
        using (Assert.Multiple())
        {
            await Assert.That(fresh.ReadFile("a.txt")).IsEqualTo("work in progress");
            await Assert.That(fresh.SnapshotWithWorkingTree()).IsEquivalentTo(snapshot);
        }
    }

    [Test]
    public async Task UncommittedChangesComeBackAfterACommit()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.WriteFile("a.txt", "work in progress");
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.StageFile("a.txt");
        repo.CommitChanges();
        repo.RestoreFrom(snapshot);

        using var fresh = repo.Reopen();
        using (Assert.Multiple())
        {
            await Assert.That(fresh.ReadFile("a.txt")).IsEqualTo("work in progress");
            await Assert.That(fresh.RetrieveStatus("a.txt")).IsEqualTo(FileStatus.ModifiedInWorkdir);
            await Assert.That(fresh.SnapshotWithWorkingTree()).IsEquivalentTo(snapshot);
        }
    }

    [Test]
    public async Task DeletedFileIsRestored()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.DeleteFile("a.txt");
        repo.RestoreFrom(snapshot);

        await Assert.That(repo.ReadFile("a.txt")).IsEqualTo("1");
    }

    [Test]
    public async Task FileDeletedInSnapshotIsDeletedAgain()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.DeleteFile("a.txt");
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.WriteFile("a.txt", "1");
        repo.RestoreFrom(snapshot);

        using var fresh = repo.Reopen();
        using (Assert.Multiple())
        {
            await Assert.That(fresh.FileExists("a.txt")).IsFalse();
            await Assert.That(fresh.RetrieveStatus("a.txt")).IsEqualTo(FileStatus.DeletedFromWorkdir);
        }
    }

    [Test]
    public async Task UntrackedFileIsRestored()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.WriteFile(Path.Combine("dir", "untracked.txt"), "untracked");
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.DeleteFile(Path.Combine("dir", "untracked.txt"));
        repo.RestoreFrom(snapshot);

        using var fresh = repo.Reopen();
        using (Assert.Multiple())
        {
            await Assert.That(fresh.ReadFile(Path.Combine("dir", "untracked.txt"))).IsEqualTo("untracked");
            await Assert.That(fresh.RetrieveStatus("dir/untracked.txt")).IsEqualTo(FileStatus.NewInWorkdir);
        }
    }

    [Test]
    public async Task UntrackedFileNotInSnapshotBlocksRestoreUnlessForced()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.WriteFile("new.txt", "new");
        var restore = () => repo.RestoreFrom(snapshot);

        using (Assert.Multiple())
        {
            var exception = await Assert.That(restore).Throws<UncommittedChangesException>();
            await Assert.That(exception!.Paths).IsEquivalentTo(["new.txt"], CollectionOrdering.Matching);
            await Assert.That(repo.FileExists("new.txt")).IsTrue();
        }

        repo.RestoreFrom(snapshot, new RestoreOptions { Force = true });
        await Assert.That(repo.FileExists("new.txt")).IsFalse();
    }

    [Test]
    public async Task IgnoredFilesAreLeftAlone()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile(".gitignore", "*.log\n");
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.WriteFile("build.log", "ignored");
        repo.RestoreFrom(snapshot);

        await Assert.That(repo.ReadFile("build.log")).IsEqualTo("ignored");
    }

    [Test]
    public async Task StagedContentIsRestoredSeparatelyFromWorkingDirectory()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "committed");
        repo.WriteFile("a.txt", "staged");
        repo.StageFile("a.txt");
        repo.WriteFile("a.txt", "in working directory");
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.StageFile("a.txt");
        repo.CommitChanges();
        repo.RestoreFrom(snapshot, new RestoreOptions { Force = true });

        using var fresh = repo.Reopen();
        var staged = fresh.Lookup<Blob>(fresh.Index["a.txt"].Id).GetContentText();
        using (Assert.Multiple())
        {
            await Assert.That(staged).IsEqualTo("staged");
            await Assert.That(fresh.ReadFile("a.txt")).IsEqualTo("in working directory");
            await Assert.That(fresh.RetrieveStatus("a.txt"))
                .IsEqualTo(FileStatus.ModifiedInIndex | FileStatus.ModifiedInWorkdir);
        }
    }

    [Test]
    public async Task RestoreIsNotBlockedWhenCurrentContentMatchesSnapshot()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.WriteFile("a.txt", "work in progress");
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.AddTag("v1");
        repo.RestoreFrom(snapshot);

        using (Assert.Multiple())
        {
            await Assert.That(repo.Tags["v1"]).IsNull();
            await Assert.That(repo.ReadFile("a.txt")).IsEqualTo("work in progress");
        }
    }

    [Test]
    public async Task WorkingTreeIsNotRestoredWhenOptionIsOff()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.WriteFile("a.txt", "work in progress");
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.WriteFile("a.txt", "changed later");
        repo.RestoreFrom(snapshot, new RestoreOptions { WorkingTree = false });

        await Assert.That(repo.ReadFile("a.txt")).IsEqualTo("changed later");
    }

    [Test]
    public async Task ExecutableBitIsRestored()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.WriteFile("script.sh", "#!/bin/sh\n");
        File.SetUnixFileMode(repo.FullPath("script.sh"), File.GetUnixFileMode(repo.FullPath("script.sh")) | UnixFileMode.UserExecute);
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.DeleteFile("script.sh");
        repo.RestoreFrom(snapshot);

        using (Assert.Multiple())
        {
            await Assert.That((File.GetUnixFileMode(repo.FullPath("script.sh")) & UnixFileMode.UserExecute))
                .IsEqualTo(UnixFileMode.UserExecute);
            await Assert.That(repo.Reopen().SnapshotWithWorkingTree()).IsEquivalentTo(snapshot);
        }
    }

    [Test]
    public async Task WorkingTreeOfUnbornRepositoryIsRestored()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.WriteFile("a.txt", "staged");
        repo.StageFile("a.txt");
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.CommitChanges();
        repo.WriteFile("b.txt", "later");
        repo.RestoreFrom(snapshot, new RestoreOptions { Force = true });

        using var fresh = repo.Reopen();
        using (Assert.Multiple())
        {
            await Assert.That(fresh.Info.IsHeadUnborn).IsTrue();
            await Assert.That(fresh.FileExists("b.txt")).IsFalse();
            await Assert.That(fresh.RetrieveStatus("a.txt")).IsEqualTo(FileStatus.NewInIndex);
        }
    }
}
