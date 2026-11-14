using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

public sealed class RestoreReferencesTests
{
    [Test]
    public async Task SnapshotContainsOtherReferences()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");
        repo.Refs.Add("refs/custom/marker", commit.Id);

        var snapshot = repo.Snapshot();

        await Assert.That(snapshot.References)
            .IsEquivalentTo(new Dictionary<string, string> { ["refs/custom/marker"] = commit.Sha });
    }

    [Test]
    public async Task SnapshotDoesNotRepeatBranchesTagsOrRemotesInReferences()
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        clone.AddTag("v1");

        await Assert.That(clone.Snapshot().References).IsEmpty();
    }

    [Test]
    public async Task RemovedReferenceIsRestored()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");
        repo.Refs.Add("refs/custom/marker", commit.Id);
        var snapshot = repo.Snapshot();

        repo.Refs.Remove("refs/custom/marker");
        repo.RestoreFrom(snapshot);

        using var reopened = repo.Reopen();
        await Assert.That(reopened.Refs["refs/custom/marker"].TargetIdentifier).IsEqualTo(commit.Sha);
    }

    [Test]
    public async Task AddedReferenceIsRemoved()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();

        repo.Refs.Add("refs/custom/marker", commit.Id);
        repo.RestoreFrom(snapshot);

        using var reopened = repo.Reopen();
        await Assert.That(reopened.Refs["refs/custom/marker"]).IsNull();
    }

    [Test]
    public async Task StashIsRestored()
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
        using (Assert.Multiple())
        {
            await Assert.That(fresh.Refs["refs/stash"].TargetIdentifier).IsEqualTo(stash.WorkTree.Sha);
            await Assert.That(fresh.Snapshot()).IsEquivalentTo(snapshot);
        }
    }

    [Test]
    public async Task NotesAreRestored()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");
        var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
        repo.Notes.Add(commit.Id, "a note", signature, signature, "commits");
        var snapshot = repo.Snapshot();

        repo.Notes.Remove(commit.Id, signature, signature, "commits");
        repo.RestoreFrom(snapshot);

        using var fresh = repo.Reopen();
        await Assert.That(fresh.Notes[commit.Id].Single().Message).IsEqualTo("a note");
    }
}
