using FluentAssertions;
using FluentAssertions.Execution;

using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

public sealed class RestoreReferencesTests
{
    [Test]
    public void SnapshotContainsOtherReferences()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");
        repo.Refs.Add("refs/custom/marker", commit.Id);

        var snapshot = repo.Snapshot();

        snapshot.References.Should().Equal(new Dictionary<string, string> { ["refs/custom/marker"] = commit.Sha });
    }

    [Test]
    public void SnapshotDoesNotRepeatBranchesTagsOrRemotesInReferences()
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        clone.AddTag("v1");

        clone.Snapshot().References.Should().BeEmpty();
    }

    [Test]
    public void RemovedReferenceIsRestored()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");
        repo.Refs.Add("refs/custom/marker", commit.Id);
        var snapshot = repo.Snapshot();

        repo.Refs.Remove("refs/custom/marker");
        repo.RestoreFrom(snapshot);

        repo.Reopen().Refs["refs/custom/marker"].TargetIdentifier.Should().Be(commit.Sha);
    }

    [Test]
    public void AddedReferenceIsRemoved()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();

        repo.Refs.Add("refs/custom/marker", commit.Id);
        repo.RestoreFrom(snapshot);

        repo.Reopen().Refs["refs/custom/marker"].Should().BeNull();
    }

    [Test]
    public void StashIsRestored()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.WriteFile("a.txt", "stashed");
        var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
        var stash = repo.Stashes.Add(signature, "work in progress");
        var snapshot = repo.Snapshot();

        repo.Stashes.Remove(0);
        repo.RestoreFrom(snapshot);

        using var fresh = repo.Reopen();
        using (new AssertionScope())
        {
            fresh.Refs["refs/stash"].TargetIdentifier.Should().Be(stash.WorkTree.Sha);
            fresh.Snapshot().Should().BeEquivalentTo(snapshot);
        }
    }

    [Test]
    public void NotesAreRestored()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");
        var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
        repo.Notes.Add(commit.Id, "a note", signature, signature, "commits");
        var snapshot = repo.Snapshot();

        repo.Notes.Remove(commit.Id, signature, signature, "commits");
        repo.RestoreFrom(snapshot);

        using var fresh = repo.Reopen();
        fresh.Notes[commit.Id].Single().Message.Should().Be("a note");
    }
}
