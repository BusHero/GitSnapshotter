using FluentAssertions;
using FluentAssertions.Execution;

namespace GitSnapshotter.UnitTests;

public sealed class SnapshotScopeTests
{
    [Theory, AutoData]
    public void DisposingTheScopeRestoresTheRepository(string branchName, string tagName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var before = repo.Snapshot();

        using (GitRepository.Preserve(repo.Info.WorkingDirectory))
        {
            repo.AddBranch(branchName);
            repo.AddTag(tagName);
            repo.CommitFile("a.txt", "2");
        }

        using var fresh = repo.Reopen();
        using (new AssertionScope())
        {
            fresh.Snapshot().Should().BeEquivalentTo(before);
            fresh.ReadFile("a.txt").Should().Be("1");
        }
    }

    [Fact]
    public void ScopeExposesTheSnapshot()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");

        using var scope = GitRepository.Preserve(repo.Info.WorkingDirectory);

        scope.Snapshot.Should().BeEquivalentTo(repo.Snapshot());
    }

    [Theory, AutoData]
    public void SecondDisposeDoesNothing(string tagName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var scope = GitRepository.Preserve(repo.Info.WorkingDirectory);
        scope.Dispose();

        repo.AddTag(tagName);
        scope.Dispose();

        repo.Tags[tagName].Should().NotBeNull();
    }

    [Fact]
    public void ScopeCanPreserveTheWorkingTree()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.WriteFile("a.txt", "work in progress");

        using (GitRepository.Preserve(
                   repo.Info.WorkingDirectory,
                   new SnapshotOptions { IncludeWorkingTree = true },
                   new RestoreOptions { Force = true }))
        {
            repo.WriteFile("a.txt", "scribbles");
            repo.WriteFile("temp.txt", "temporary");
        }

        using (new AssertionScope())
        {
            repo.ReadFile("a.txt").Should().Be("work in progress");
            repo.FileExists("temp.txt").Should().BeFalse();
        }
    }
}
