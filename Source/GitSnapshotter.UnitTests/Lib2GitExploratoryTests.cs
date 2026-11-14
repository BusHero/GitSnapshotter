using Constants;


using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

[Category(TestConstants.Traits.Values.DISCOVERY)]
public sealed class Lib2GitExploratoryTests
{
    [Test]
    public async Task EmptyRepositoryContainsNoBranches()
    {
        var path = GitTasks.GetTempPath();

        Repository.Init(path);

        using var repo = new Repository(path);

        await Assert.That(repo.Branches).IsEmpty();
    }

    [Test]
    public async Task RepositoryInitiation()
    {
        var path = GitTasks.GetTempPath();

        var repo = Repository.Init(path);

        using (Assert.Multiple())
        {
            await Assert.That(repo).StartsWith(path);
            await Assert.That(Directory.Exists(path)).IsTrue();
        }
    }

    [Test, AutoData]
    public async Task CommitFile(
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

        await Assert.That(commit.MessageShort).IsEqualTo(commitMessage);
    }

    [Test, AutoData]
    public async Task CommitedFileIsStaged(
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

        await Assert.That(repo.Index[filename].StageLevel).IsEqualTo(StageLevel.Staged);
    }

    [Test, AutoData]
    public async Task FileNotAddedToIndexIsNotPresent(
        string filename,
        string[] contents)
    {
        var path = GitTasks.GetTempPath();

        Repository.Init(path);

        File.WriteAllLines(Path.Combine(path, filename), contents);
        using var repo = new Repository(path);

        await Assert.That(repo.Index.Select(x => x.Path)).DoesNotContain(filename);
    }

    [Test, AutoData]
    public async Task FileAddedToIndexButNotToFilesystemIsPresent(
        string filename,
        string[] contents)
    {
        var path = GitTasks.GetTempPath();

        Repository.Init(path);

        File.WriteAllLines(Path.Combine(path, filename), contents);
        using var repo = new Repository(path);
        repo.Index.Add(filename);

        await Assert.That(repo.Index[filename].StageLevel).IsEqualTo(StageLevel.Staged);
    }

    [Test, AutoData]
    public async Task Branch(
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

        using (Assert.Multiple())
        {
            await Assert.That(branch.CanonicalName).IsEqualTo("refs/heads/master");
            await Assert.That(branch.FriendlyName).IsEqualTo("master");
        }
    }

    [Test, AutoData]
    public async Task CreateNewBranch(
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

        await Assert.That(repo.Branches.Select(x => x.FriendlyName)).Contains(branchName);
    }

    [Test, AutoData]
    public async Task SwitchBranches(
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

        await Assert.That(repo.Head.FriendlyName).IsEqualTo(branchName);
    }
}
