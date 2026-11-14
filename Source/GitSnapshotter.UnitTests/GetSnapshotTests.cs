using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

public sealed class GetSnapshotTests
{
    [Test]
    public async Task SnapshotOfInitialRepositoryContainsDefaultBranch()
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.AddFileToRepository();

        repository.CommitChanges();

        var snapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);

        await Assert.That(snapshot.Branches.FirstOrDefault(x => x.Name == "master")).IsNotNull();
    }

    [Test, AutoData]
    public async Task SnapshotContainsAllCreatedBranches(string branchName)
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.AddFileToRepository();
        repository.CommitChanges();
        repository.AddBranch(branchName);

        var snapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);

        await Assert.That(snapshot.Branches.FirstOrDefault(x => x.Name == branchName)).IsNotNull();
    }

    [Test, AutoData]
    public async Task HeadContainsMaster(string branchName)
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.AddFileToRepository();
        repository.CommitChanges();
        repository.AddBranch(branchName);

        var snapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);

        await Assert.That(snapshot.Head).IsEqualTo("master");
    }

    [Test, AutoData]
    public async Task HeadContainsCheckoutBranch(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.AddFileToRepository();
        repo.CommitChanges();
        repo.AddBranch(branchName);
        repo.CheckoutBranch(branchName);

        var snapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        await Assert.That(snapshot.Head).IsEqualTo(branchName);
    }

    [Test]
    public async Task BranchesContainsCommits()
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.AddFileToRepository();
        repository.CommitChanges();

        var snapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);

        var commit = snapshot.Branches.First(x => x.Name == "master").Tip;

        await Assert.That(repository.IsValidCommit(commit)).IsTrue();
    }

    [Test, AutoData]
    public async Task SnapshotContainsRemotes(string remoteName)
    {
        using var original = GitTasks.CreateTemporaryGitRepository();
        original.AddFileToRepository();
        original.CommitChanges();

        using var remote = GitTasks.CreateTemporaryGitRepository();
        remote.AddFileToRepository();
        remote.CommitChanges();

        var remoteUrl = remote.Info.Path;
        original.AddRemote(remoteName, remoteUrl);

        var snapshot = GitRepository.GetSnapshot(original.Info.WorkingDirectory);

        await Assert.That(snapshot.Remotes.First(x => x.Name == remoteName).Url).IsEqualTo(remoteUrl);
    }

    [Test, AutoData]
    public async Task SnapshotContainsRemoteBranches(string remoteName)
    {
        using var original = GitTasks.CreateTemporaryGitRepository();
        original.AddFileToRepository();
        original.CommitChanges();

        using var remote = GitTasks.CreateTemporaryGitRepository();
        remote.AddFileToRepository();
        remote.CommitChanges();

        var remoteUrl = remote.Info.Path;
        original.AddRemote(remoteName, remoteUrl);
        Commands.Fetch(original, remoteName, [], new FetchOptions(), null);

        var snapshot = GitRepository.GetSnapshot(original.Info.WorkingDirectory);

        var branches = snapshot.Remotes.First(x => x.Name == remoteName).Branches;
        await Assert.That(branches.Keys).IsEquivalentTo([$"{remoteName}/master"], CollectionOrdering.Matching);
    }

    [Test, AutoData]
    public async Task SnapshotDoesNotContainRemoteBranches(string remoteName)
    {
        using var original = GitTasks.CreateTemporaryGitRepository();
        original.AddFileToRepository();
        original.CommitChanges();

        using var remote = GitTasks.CreateTemporaryGitRepository();
        remote.AddFileToRepository();
        remote.CommitChanges();

        var remoteUrl = remote.Info.Path;
        original.AddRemote(remoteName, remoteUrl);
        Commands.Fetch(original, remoteName, [], new FetchOptions(), null);

        var snapshot = GitRepository.GetSnapshot(original.Info.WorkingDirectory);

        var item2 = await Assert.That(snapshot.Branches).HasSingleItem();
        await Assert.That(item2.Name).IsEqualTo("master");
    }

    [Test, AutoData]
    public async Task RemoteTipIsValidSha(string remoteName)
    {
        using var original = GitTasks.CreateTemporaryGitRepository();
        original.AddFileToRepository();
        original.CommitChanges();

        using var remote = GitTasks.CreateTemporaryGitRepository();
        remote.AddFileToRepository();
        remote.CommitChanges();

        var remoteUrl = remote.Info.Path;
        original.AddRemote(remoteName, remoteUrl);
        Commands.Fetch(original, remoteName, [], new FetchOptions(), null);

        var snapshot = GitRepository.GetSnapshot(original.Info.WorkingDirectory);

        var commit = snapshot.Remotes.First(x => x.Name == remoteName)
            .Branches[$"{remoteName}/master"];

        using (Assert.Multiple())
        {
            await Assert.That(original.IsValidCommit(commit)).IsTrue();
            await Assert.That(remote.IsValidCommit(commit)).IsTrue();
        }
    }

    [Test, AutoData]
    public async Task BranchShowsFollowedBranch(string remoteName, string branchName)
    {
        using var original = GitTasks.CreateTemporaryGitRepository();
        original.AddFileToRepository();
        original.CommitChanges();

        using var remote = GitTasks.CreateTemporaryGitRepository();
        remote.AddFileToRepository();
        remote.CommitChanges();
        remote.AddBranch(branchName);

        var remoteUrl = remote.Info.Path;
        original.AddRemote(remoteName, remoteUrl);
        Commands.Fetch(original, remoteName, [], new FetchOptions(), null);
        original.Branches.Update(
            original.Branches["master"],
            x => x.TrackedBranch = $"refs/remotes/{remoteName}/{branchName}",
            x => x.Remote = remoteName);

        var snapshot = GitRepository.GetSnapshot(original.Info.WorkingDirectory);

        using (Assert.Multiple())
        {
            var branch = snapshot.Branches.First(x => x.Name == "master");

            await Assert.That(branch.TrackedBranch).IsEqualTo($"{remoteName}/{branchName}");
            await Assert.That(branch.RemoteName).IsEqualTo(remoteName);
        }
    }

    [Test, AutoData]
    public async Task SnapshotContainsTags(string tagName)
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.AddFileToRepository();
        repository.CommitChanges();
        repository.Tags.Add(tagName, repository.Head.Tip);

        var snapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);

        var tag = snapshot.Tags.First(x => x.Name == tagName);
        await Assert.That(repository.IsValidCommit(tag.Target)).IsTrue();
    }

    [Test, AutoData]
    public async Task AnnotatedTagContainsMessage(string tagName, string tagMessage)
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.AddFileToRepository();
        repository.CommitChanges();
        var signature = repository.Config.BuildSignature(DateTimeOffset.Now);
        repository.Tags.Add(tagName, repository.Head.Tip, signature, tagMessage);

        var snapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);

        await Assert.That(snapshot.Tags.First(x => x.Name == tagName).Message!.TrimEnd()).IsEqualTo(tagMessage);
    }

    [Test, AutoData]
    public async Task NonAnnotatedTagContainsNullForMessage(string tagName)
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.AddFileToRepository();
        repository.CommitChanges();
        repository.Tags.Add(tagName, repository.Head.Tip);

        var snapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);

        await Assert.That(snapshot.Tags.First(x => x.Name == tagName).Message).IsNull();
    }
}