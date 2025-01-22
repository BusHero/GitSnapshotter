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

    private static string GetTempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        return path;
    }
}