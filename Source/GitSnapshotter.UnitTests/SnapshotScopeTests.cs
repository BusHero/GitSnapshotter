namespace GitSnapshotter.UnitTests;

public sealed class SnapshotScopeTests
{
    [Test, AutoData]
    public async Task DisposingTheScopeRestoresTheRepository(string branchName, string tagName)
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
        using (Assert.Multiple())
        {
            await Assert.That(fresh.Snapshot()).IsEquivalentTo(before);
            await Assert.That(fresh.ReadFile("a.txt")).IsEqualTo("1");
        }
    }

    [Test]
    public async Task ScopeExposesTheSnapshot()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");

        using var scope = GitRepository.Preserve(repo.Info.WorkingDirectory);

        await Assert.That(scope.Snapshot).IsEquivalentTo(repo.Snapshot());
    }

    [Test, AutoData]
    public async Task SecondDisposeDoesNothing(string tagName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var scope = GitRepository.Preserve(repo.Info.WorkingDirectory);
        scope.Dispose();

        repo.AddTag(tagName);
        scope.Dispose();

        await Assert.That(repo.Tags[tagName]).IsNotNull();
    }

    [Test]
    public async Task ScopeCanPreserveTheWorkingTree()
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

        using (Assert.Multiple())
        {
            await Assert.That(repo.ReadFile("a.txt")).IsEqualTo("work in progress");
            await Assert.That(repo.FileExists("temp.txt")).IsFalse();
        }
    }
}
