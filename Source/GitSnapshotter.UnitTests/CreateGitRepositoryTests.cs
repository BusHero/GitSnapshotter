using FluentAssertions;
using FluentAssertions.Execution;

using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

public class CreateGitRepositoryTests
{
    [Fact]
    public void SnapshotOfInitialRepositoryContainsDefaultBranch()
    {
        var snapshot = GitRepository.GetSnapshot();

        snapshot.Branches.Should().Contain("master");
    }

    [Fact]
    public void CreateTemporaryGitRepositoryReturnsTemporaryGitRepository()
    {
        var pathToRepo = CreateTemporaryGitRepository();

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
        var pathToRepo = CreateTemporaryGitRepository();
        
        var filename = AddFileToRepository(pathToRepo);
        
        File.Exists(Path.Combine(pathToRepo, filename)).Should().BeTrue();

        using var repo = new Repository(pathToRepo);
        repo.Index.Index().Select(x => x.Item.Path).Should().Contain(filename);
    }

    private static string AddFileToRepository(string pathToRepo)
    {
        var filename = Guid.NewGuid().ToString("N");

        File.WriteAllText(Path.Combine(pathToRepo, filename), filename);
        
        var repository = new Repository(pathToRepo);
        repository.Index.Add(filename);
        repository.Index.Write();
        
        return filename;
    }

    private static string CreateTemporaryGitRepository()
    {
        var path = GetTempDirectoryName();
        
        Repository.Init(path);
        
        return path;
    }

    private static string GetTempDirectoryName()
    {
        return Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
    }
}