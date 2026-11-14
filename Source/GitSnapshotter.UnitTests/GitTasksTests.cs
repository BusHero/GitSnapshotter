using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

public sealed class GitTasksTests
{
    [Test, AutoData]
    public async Task AddBranchAddsABranchTest(string branch)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();

        repo.AddFileToRepository();

        repo.CommitChanges();
        repo.AddBranch(branch);

        await Assert.That(repo.Branches.Select(x => x.FriendlyName)).Contains(branch);
    }

    [Test]
    public async Task CreateTemporaryGitRepositoryReturnsTemporaryGitRepository()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var pathToRepo = repo.Info.WorkingDirectory;

        using (Assert.Multiple())
        {
            await Assert.That(Repository.IsValid(pathToRepo)).IsTrue();
            await Assert.That(pathToRepo).StartsWith(Path.GetTempPath());
            await Assert.That(pathToRepo).DoesNotEndWith(".git");
        }
    }

    [Test]
    public async Task AddFileToRepositoryAddsFileToRepository()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();

        var filename = repo.AddFileToRepository();

        await Assert.That(File.Exists(Path.Combine(repo.Info.WorkingDirectory, filename))).IsTrue();

        await Assert.That(repo.Index.Select(x => x.Path)).Contains(filename);
    }

    [Test]
    public async Task CommitCommitsTest()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();

        repo.AddFileToRepository();

        repo.CommitChanges();

        await Assert.That(repo.Commits).HasSingleItem();
    }

    [Test, AutoData]
    public async Task IsValidCommit_ValidCommitReturnsTrue(
        string filename,
        string[] contents,
        string commitMessage)
    {
        var path = GitTasks.GetTempPath();

        Repository.Init(path);

        File.WriteAllLines(Path.Combine(path, filename), contents);
        using var repo = new Repository(path);

        repo.Index.Add(filename);
        repo.Index.Write();

        var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
        var commit = repo.Commit(commitMessage, signature, signature);

        var sha = commit.Sha;

        var result = repo.IsValidCommit(sha);

        await Assert.That(result).IsTrue();
    }

    [Test, AutoData]
    public async Task IsValidCommit_InvalidCommitReturnsFalse(
        string filename,
        string[] contents,
        string commitMessage)
    {
        var path = GitTasks.GetTempPath();

        Repository.Init(path);

        File.WriteAllLines(Path.Combine(path, filename), contents);
        using var repo = new Repository(path);

        repo.Index.Add(filename);
        repo.Index.Write();

        var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
        repo.Commit(commitMessage, signature, signature);

        var result = repo.IsValidCommit(Guid.NewGuid().ToString("N"));

        await Assert.That(result).IsFalse();
    }
}
