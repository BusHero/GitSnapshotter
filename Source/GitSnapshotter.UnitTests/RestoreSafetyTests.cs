using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

public sealed class RestoreSafetyTests
{
    private const string UnknownSha = "0123456789abcdef0123456789abcdef01234567";

    [Test, AutoData]
    public async Task MissingBranchTipFailsWithoutChangingAnything(string branchName, string tagName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();
        snapshot = snapshot with
        {
            Branches = [.. snapshot.Branches, new Branch { Name = branchName, Tip = UnknownSha }],
        };
        repo.AddTag(tagName);
        var before = repo.Snapshot();

        var restore = () => repo.RestoreFrom(snapshot);

        using (Assert.Multiple())
        {
            var exception = await Assert.That(restore).Throws<MissingObjectsException>();
            await Assert.That(exception!.MissingObjects).IsEquivalentTo([UnknownSha], CollectionOrdering.Matching);
            using var reopened = repo.Reopen();
            await Assert.That(reopened.Snapshot()).IsEquivalentTo(before);
        }
    }

    [Test]
    public async Task InvalidShaIsReportedAsMissing()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot() with
        {
            Tags = [new Tag { Name = "broken", Target = "not-a-sha" }],
        };

        var restore = () => repo.RestoreFrom(snapshot);

        var exception2 = await Assert.That(restore).Throws<MissingObjectsException>();
        await Assert.That(exception2!.MissingObjects).IsEquivalentTo(["not-a-sha"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task MissingObjectsInSkippedPartsAreIgnored()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot() with
        {
            Tags = [new Tag { Name = "broken", Target = UnknownSha }],
        };

        var restore = () => repo.RestoreFrom(snapshot, new RestoreOptions { Tags = false });

        await Assert.That(restore).ThrowsNothing();
    }

    [Test, AutoData]
    public async Task TagsAreLeftAloneWhenOptionIsOff(string tagName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();

        repo.AddTag(tagName);
        repo.RestoreFrom(snapshot, new RestoreOptions { Tags = false });

        await Assert.That(repo.Tags[tagName]).IsNotNull();
    }

    [Test, AutoData]
    public async Task BranchesAreLeftAloneWhenOptionIsOff(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();

        repo.AddBranch(branchName);
        repo.RestoreFrom(snapshot, new RestoreOptions { Branches = false });

        await Assert.That(repo.Branches[branchName]).IsNotNull();
    }

    [Test, AutoData]
    public async Task HeadIsLeftAloneWhenOptionIsOff(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.AddBranch(branchName);
        var snapshot = repo.Snapshot();

        repo.CheckoutBranch(branchName);
        repo.RestoreFrom(snapshot, new RestoreOptions { Head = false });

        using var fresh = repo.Reopen();
        using (Assert.Multiple())
        {
            await Assert.That(fresh.Info.IsHeadDetached).IsFalse();
            await Assert.That(fresh.Head.FriendlyName).IsEqualTo(branchName);
        }
    }

    [Test, AutoData]
    public async Task HeadStaysDetachedWhenItsBranchIsRemovedAndHeadIsNotRestored(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();

        repo.AddBranch(branchName);
        repo.CheckoutBranch(branchName);
        repo.RestoreFrom(snapshot, new RestoreOptions { Head = false });

        using var fresh = repo.Reopen();
        using (Assert.Multiple())
        {
            await Assert.That(fresh.Branches[branchName]).IsNull();
            await Assert.That(fresh.Info.IsHeadDetached).IsTrue();
            await Assert.That(fresh.Head.Tip.Sha).IsEqualTo(commit.Sha);
        }
    }

    [Test]
    public async Task RestoreWorksOnAnOpenRepository()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = GitRepository.GetSnapshot(repo);

        repo.AddTag("v1");
        repo.AddBranch("feature");
        GitRepository.Restore(repo, snapshot);

        await Assert.That(GitRepository.GetSnapshot(repo)).IsEquivalentTo(snapshot);
    }

    [Test, AutoData]
    public async Task BranchNamesWithSlashesAreRestored(string suffix)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.AddBranch($"feature/{suffix}");
        var snapshot = repo.Snapshot();

        repo.Branches.Remove($"feature/{suffix}");
        repo.RestoreFrom(snapshot);

        using var reopened = repo.Reopen();
        await Assert.That(reopened.Snapshot()).IsEquivalentTo(snapshot);
    }
}
