using FluentAssertions;
using FluentAssertions.Execution;

using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

public sealed class RestoreSafetyTests
{
    private const string UnknownSha = "0123456789abcdef0123456789abcdef01234567";

    [Test, AutoData]
    public void MissingBranchTipFailsWithoutChangingAnything(string branchName, string tagName)
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

        using (new AssertionScope())
        {
            restore.Should().Throw<MissingObjectsException>().Which.MissingObjects.Should().Equal(UnknownSha);
            repo.Reopen().Snapshot().Should().BeEquivalentTo(before);
        }
    }

    [Test]
    public void InvalidShaIsReportedAsMissing()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot() with
        {
            Tags = [new Tag { Name = "broken", Target = "not-a-sha" }],
        };

        var restore = () => repo.RestoreFrom(snapshot);

        restore.Should().Throw<MissingObjectsException>().Which.MissingObjects.Should().Equal("not-a-sha");
    }

    [Test]
    public void MissingObjectsInSkippedPartsAreIgnored()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot() with
        {
            Tags = [new Tag { Name = "broken", Target = UnknownSha }],
        };

        var restore = () => repo.RestoreFrom(snapshot, new RestoreOptions { Tags = false });

        restore.Should().NotThrow();
    }

    [Test, AutoData]
    public void TagsAreLeftAloneWhenOptionIsOff(string tagName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();

        repo.AddTag(tagName);
        repo.RestoreFrom(snapshot, new RestoreOptions { Tags = false });

        repo.Tags[tagName].Should().NotBeNull();
    }

    [Test, AutoData]
    public void BranchesAreLeftAloneWhenOptionIsOff(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();

        repo.AddBranch(branchName);
        repo.RestoreFrom(snapshot, new RestoreOptions { Branches = false });

        repo.Branches[branchName].Should().NotBeNull();
    }

    [Test, AutoData]
    public void HeadIsLeftAloneWhenOptionIsOff(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.AddBranch(branchName);
        var snapshot = repo.Snapshot();

        repo.CheckoutBranch(branchName);
        repo.RestoreFrom(snapshot, new RestoreOptions { Head = false });

        using var fresh = repo.Reopen();
        using (new AssertionScope())
        {
            fresh.Info.IsHeadDetached.Should().BeFalse();
            fresh.Head.FriendlyName.Should().Be(branchName);
        }
    }

    [Test, AutoData]
    public void HeadStaysDetachedWhenItsBranchIsRemovedAndHeadIsNotRestored(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var commit = repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();

        repo.AddBranch(branchName);
        repo.CheckoutBranch(branchName);
        repo.RestoreFrom(snapshot, new RestoreOptions { Head = false });

        using var fresh = repo.Reopen();
        using (new AssertionScope())
        {
            fresh.Branches[branchName].Should().BeNull();
            fresh.Info.IsHeadDetached.Should().BeTrue();
            fresh.Head.Tip.Sha.Should().Be(commit.Sha);
        }
    }

    [Test]
    public void RestoreWorksOnAnOpenRepository()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = GitRepository.GetSnapshot(repo);

        repo.AddTag("v1");
        repo.AddBranch("feature");
        GitRepository.Restore(repo, snapshot);

        GitRepository.GetSnapshot(repo).Should().BeEquivalentTo(snapshot);
    }

    [Test, AutoData]
    public void BranchNamesWithSlashesAreRestored(string suffix)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.AddBranch($"feature/{suffix}");
        var snapshot = repo.Snapshot();

        repo.Branches.Remove($"feature/{suffix}");
        repo.RestoreFrom(snapshot);

        repo.Reopen().Snapshot().Should().BeEquivalentTo(snapshot);
    }
}
