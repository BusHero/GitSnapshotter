using FluentAssertions;
using FluentAssertions.Execution;

using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

public sealed class GitTasksTests
{
    [Theory, AutoData]
    public void AddBranchAddsABranchTest(string branch)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();

        repo.AddFileToRepository();

        repo.CommitChanges();
        repo.AddBranch(branch);

        repo.Branches.Select(x => x.FriendlyName).Should().Contain(branch);
    }

    [Fact]
    public void CreateTemporaryGitRepositoryReturnsTemporaryGitRepository()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        var pathToRepo = repo.Info.WorkingDirectory;

        using (new AssertionScope())
        {
            Repository.IsValid(pathToRepo).Should().BeTrue();
            pathToRepo.Should().StartWith(Path.GetTempPath());
            pathToRepo.Should().NotEndWith(".git");
        }
    }

    [Fact]
    public void AddFileToRepositoryAddsFileToRepository()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();

        var filename = repo.AddFileToRepository();

        File.Exists(Path.Combine(repo.Info.WorkingDirectory, filename)).Should().BeTrue();

        repo.Index.Index().Select(x => x.Item.Path).Should().Contain(filename);
    }

    [Fact]
    public void CommitCommitsTest()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();

        repo.AddFileToRepository();

        repo.CommitChanges();

        repo.Commits.Should().ContainSingle();
    }

    [Theory, AutoData]
    public void IsValidCommit_ValidCommitReturnsTrue(
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

        result.Should().BeTrue();
    }

    [Theory, AutoData]
    public void IsValidCommit_InvalidCommitReturnsFalse(
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

        result.Should().BeFalse();
    }
}