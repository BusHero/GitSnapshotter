using Constants;

using FluentAssertions;
using FluentAssertions.Execution;

using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

[Trait(TestConstants.Traits.Names.CATEGORY, TestConstants.Traits.Values.DISCOVERY)]
public sealed class Lib2GitExploratoryTests
{
    [Fact]
    public void EmptyRepositoryContainsNoBranches()
    {
        var path = GitTasks.GetTempPath();

        Repository.Init(path);

        using var repo = new Repository(path);

        repo.Branches.Should().BeEmpty();
    }

    [Fact]
    public void RepositoryInitiation()
    {
        var path = GitTasks.GetTempPath();

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
        var path = GitTasks.GetTempPath();

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
    public void CommitedFileIsStaged(
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

        repo.Index[filename].StageLevel.Should().Be(StageLevel.Staged);
    }

    [Theory, AutoData]
    public void FileNotAddedToIndexIsNotPresent(
        string filename,
        string[] contents)
    {
        var path = GitTasks.GetTempPath();

        Repository.Init(path);

        File.WriteAllLines(Path.Combine(path, filename), contents);
        using var repo = new Repository(path);

        repo.Index.Select(x => x.Path).Should().NotContain(filename);
    }

    [Theory, AutoData]
    public void FileAddedToIndexButNotToFilesystemIsPresent(
        string filename,
        string[] contents)
    {
        var path = GitTasks.GetTempPath();

        Repository.Init(path);

        File.WriteAllLines(Path.Combine(path, filename), contents);
        using var repo = new Repository(path);
        repo.Index.Add(filename);

        repo.Index[filename].StageLevel.Should().Be(StageLevel.Staged);
    }

    [Theory, AutoData]
    public void Branch(
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
        var path = GitTasks.GetTempPath();

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

    [Theory, AutoData]
    public void SwitchBranches(
        string filename,
        string[] contents,
        string commitMessage,
        string branchName)
    {
        var path = GitTasks.GetTempPath();

        Repository.Init(path);

        File.WriteAllLines(Path.Combine(path, filename), contents);
        using var repo = new Repository(path);

        repo.Index.Add(filename);
        repo.Index.Write();

        var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
        repo.Commit(commitMessage, signature, signature);

        var branch = repo.CreateBranch(branchName);

        Commands.Checkout(repo, branch);

        repo.Head.FriendlyName.Should().Be(branchName);
    }
}