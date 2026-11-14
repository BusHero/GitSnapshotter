using FluentAssertions;
using FluentAssertions.Execution;

using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

public sealed class WorkingTreeTests
{
    [Fact]
    public void WorkingTreeIsNotCapturedByDefault()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");

        repo.Snapshot().WorkingTree.Should().BeNull();
    }

    [Fact]
    public void CleanWorkingTreeMatchesHeadTree()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");

        var state = repo.SnapshotWithWorkingTree().WorkingTree!;

        using (new AssertionScope())
        {
            state.IndexTree.Should().Be(commit.Tree.Sha);
            state.WorkingDirectoryTree.Should().Be(commit.Tree.Sha);
        }
    }

    [Fact]
    public void CapturingDoesNotChangeTheWorkingTree()
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
        using (new AssertionScope())
        {
            fresh.RetrieveStatus().Select(e => (e.FilePath, e.State)).Should().BeEquivalentTo(statusBefore);
            fresh.Refs.Select(r => (r.CanonicalName, r.TargetIdentifier)).Should().BeEquivalentTo(refsBefore);
        }
    }

    [Fact]
    public void ModifiedFileIsRestored()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.WriteFile("a.txt", "work in progress");
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.WriteFile("a.txt", "something else");
        repo.RestoreFrom(snapshot, new RestoreOptions { Force = true });

        using var fresh = repo.Reopen();
        using (new AssertionScope())
        {
            fresh.ReadFile("a.txt").Should().Be("work in progress");
            fresh.SnapshotWithWorkingTree().Should().BeEquivalentTo(snapshot);
        }
    }

    [Fact]
    public void UncommittedChangesComeBackAfterACommit()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.WriteFile("a.txt", "work in progress");
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.StageFile("a.txt");
        repo.CommitChanges();
        repo.RestoreFrom(snapshot);

        using var fresh = repo.Reopen();
        using (new AssertionScope())
        {
            fresh.ReadFile("a.txt").Should().Be("work in progress");
            fresh.RetrieveStatus("a.txt").Should().Be(FileStatus.ModifiedInWorkdir);
            fresh.SnapshotWithWorkingTree().Should().BeEquivalentTo(snapshot);
        }
    }

    [Fact]
    public void DeletedFileIsRestored()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.DeleteFile("a.txt");
        repo.RestoreFrom(snapshot);

        repo.ReadFile("a.txt").Should().Be("1");
    }

    [Fact]
    public void FileDeletedInSnapshotIsDeletedAgain()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.DeleteFile("a.txt");
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.WriteFile("a.txt", "1");
        repo.RestoreFrom(snapshot);

        using var fresh = repo.Reopen();
        using (new AssertionScope())
        {
            fresh.FileExists("a.txt").Should().BeFalse();
            fresh.RetrieveStatus("a.txt").Should().Be(FileStatus.DeletedFromWorkdir);
        }
    }

    [Fact]
    public void UntrackedFileIsRestored()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.WriteFile(Path.Combine("dir", "untracked.txt"), "untracked");
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.DeleteFile(Path.Combine("dir", "untracked.txt"));
        repo.RestoreFrom(snapshot);

        using var fresh = repo.Reopen();
        using (new AssertionScope())
        {
            fresh.ReadFile(Path.Combine("dir", "untracked.txt")).Should().Be("untracked");
            fresh.RetrieveStatus("dir/untracked.txt").Should().Be(FileStatus.NewInWorkdir);
        }
    }

    [Fact]
    public void UntrackedFileNotInSnapshotBlocksRestoreUnlessForced()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.WriteFile("new.txt", "new");
        var restore = () => repo.RestoreFrom(snapshot);

        using (new AssertionScope())
        {
            restore.Should().Throw<UncommittedChangesException>().Which.Paths.Should().Equal("new.txt");
            repo.FileExists("new.txt").Should().BeTrue();
        }

        repo.RestoreFrom(snapshot, new RestoreOptions { Force = true });
        repo.FileExists("new.txt").Should().BeFalse();
    }

    [Fact]
    public void IgnoredFilesAreLeftAlone()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile(".gitignore", "*.log\n");
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.WriteFile("build.log", "ignored");
        repo.RestoreFrom(snapshot);

        repo.ReadFile("build.log").Should().Be("ignored");
    }

    [Fact]
    public void StagedContentIsRestoredSeparatelyFromWorkingDirectory()
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
        using (new AssertionScope())
        {
            staged.Should().Be("staged");
            fresh.ReadFile("a.txt").Should().Be("in working directory");
            fresh.RetrieveStatus("a.txt").Should().Be(FileStatus.ModifiedInIndex | FileStatus.ModifiedInWorkdir);
        }
    }

    [Fact]
    public void RestoreIsNotBlockedWhenCurrentContentMatchesSnapshot()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.WriteFile("a.txt", "work in progress");
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.AddTag("v1");
        repo.RestoreFrom(snapshot);

        using (new AssertionScope())
        {
            repo.Tags["v1"].Should().BeNull();
            repo.ReadFile("a.txt").Should().Be("work in progress");
        }
    }

    [Fact]
    public void WorkingTreeIsNotRestoredWhenOptionIsOff()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.WriteFile("a.txt", "work in progress");
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.WriteFile("a.txt", "changed later");
        repo.RestoreFrom(snapshot, new RestoreOptions { WorkingTree = false });

        repo.ReadFile("a.txt").Should().Be("changed later");
    }

    [Fact]
    public void ExecutableBitIsRestored()
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

        using (new AssertionScope())
        {
            (File.GetUnixFileMode(repo.FullPath("script.sh")) & UnixFileMode.UserExecute).Should().Be(UnixFileMode.UserExecute);
            repo.Reopen().SnapshotWithWorkingTree().Should().BeEquivalentTo(snapshot);
        }
    }

    [Fact]
    public void WorkingTreeOfUnbornRepositoryIsRestored()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.WriteFile("a.txt", "staged");
        repo.StageFile("a.txt");
        var snapshot = repo.SnapshotWithWorkingTree();

        repo.CommitChanges();
        repo.WriteFile("b.txt", "later");
        repo.RestoreFrom(snapshot, new RestoreOptions { Force = true });

        using var fresh = repo.Reopen();
        using (new AssertionScope())
        {
            fresh.Info.IsHeadUnborn.Should().BeTrue();
            fresh.FileExists("b.txt").Should().BeFalse();
            fresh.RetrieveStatus("a.txt").Should().Be(FileStatus.NewInIndex);
        }
    }
}
