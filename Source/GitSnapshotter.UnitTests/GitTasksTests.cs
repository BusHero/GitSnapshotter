using FluentAssertions;
using FluentAssertions.Execution;

using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

public class GitTasksTests
{
    [Theory, AutoData]
    public void AddBranchAddsABranchTest(string branch)
    {
        var pathToRepo = GitTasks.CreateTemporaryGitRepository();

        GitTasks.AddFileToRepository(pathToRepo);

        GitTasks.CommitChanges(pathToRepo);
        GitTasks.AddBranch(pathToRepo, branch);

        using var repo = new Repository(pathToRepo);
        repo.Branches.Select(x => x.FriendlyName).Should().Contain(branch);
    }

    [Fact]
    public void CreateTemporaryGitRepositoryReturnsTemporaryGitRepository()
    {
        var pathToRepo = GitTasks.CreateTemporaryGitRepository();

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
        var pathToRepo = GitTasks.CreateTemporaryGitRepository();

        var filename = GitTasks.AddFileToRepository(pathToRepo);

        File.Exists(Path.Combine(pathToRepo, filename)).Should().BeTrue();

        using var repo = new Repository(pathToRepo);
        repo.Index.Index().Select(x => x.Item.Path).Should().Contain(filename);
    }

    [Fact]
    public void CommitCommitsTest()
    {
        var pathToRepo = GitTasks.CreateTemporaryGitRepository();

        GitTasks.AddFileToRepository(pathToRepo);

        GitTasks.CommitChanges(pathToRepo);

        using var repo = new Repository(pathToRepo);

        repo.Commits.Should().ContainSingle();
    }
}