using FluentAssertions;
using FluentAssertions.Execution;

using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

[Trait("Category", "Discovery")]
public class Lib2GitExploratoryTests
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

    [Theory, AutoData]
    public void CommitFile(
        string filename,
        string[] contents,
        string commitMessage)
    {
        var path = GetTempPath();

        Repository.Init(path);

        File.WriteAllLines(Path.Combine(path, filename), contents);
        using var repo = new Repository(path);

        repo.Index.Add(filename);
        repo.Index.Write();

        var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
        var commit = repo.Commit(commitMessage, signature, signature);

        commit.MessageShort.Should().Be(commitMessage);
    }

    [Theory, AutoData]
    public void Branch(
        string filename,
        string[] contents,
        string commitMessage)
    {
        var path = GetTempPath();

        Repository.Init(path);

        File.WriteAllLines(Path.Combine(path, filename), contents);
        using var repo = new Repository(path);

        repo.Index.Add(filename);
        repo.Index.Write();

        var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
        repo.Commit(commitMessage, signature, signature);

        var branch = repo.Head;

        using (new AssertionScope())
        {
            branch.CanonicalName.Should().Be("refs/heads/master");
            branch.FriendlyName.Should().Be("master");
        }
    }
    
    [Theory, AutoData]
    public void CreateNewBranch(
        string filename,
        string[] contents,
        string commitMessage,
        string branchName)
    {
        var path = GetTempPath();

        Repository.Init(path);

        File.WriteAllLines(Path.Combine(path, filename), contents);
        using var repo = new Repository(path);

        repo.Index.Add(filename);
        repo.Index.Write();

        var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
        repo.Commit(commitMessage, signature, signature);
        
        repo.CreateBranch(branchName);
        
        repo.Branches.Select(x => x.FriendlyName).Should().Contain(branchName);
    }

    private static string GetTempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        return path;
    }
}