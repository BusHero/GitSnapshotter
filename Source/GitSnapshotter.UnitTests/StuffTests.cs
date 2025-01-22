using FluentAssertions;
using FluentAssertions.Execution;

using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

public class StuffTests
{
    [Fact]
    public void RepositoryInitiation()
    {
        var path = GetTempPath();

        var repo = Repository.Init(path);

        using (new AssertionScope())
        {
            repo.Should().StartWith(path);
            Directory.Exists(path).Should().BeTrue();
        }
    }

    [Fact]
    public void CommitFile()
    {
        var path = GetTempPath();

        Repository.Init(path);
        
        File.WriteAllLines(Path.Combine(path, "test.txt"), ["test"]);
        using var repo = new Repository(path);
        
        repo.Index.Add("test.txt");
        repo.Index.Write();
        
        var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
        repo.Commit("test", signature, signature);
    }

    private static string GetTempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        return path;
    }
}