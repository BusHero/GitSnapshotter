using FluentAssertions;
using FluentAssertions.Execution;

using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

public class GetSnapshotTests
{
    [Fact]
    public void SnapshotOfInitialRepositoryContainsDefaultBranch()
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.AddFileToRepository();

        repository.CommitChanges();

        var snapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);

        snapshot.Branches.FirstOrDefault(x => x.Name == "master").Should().NotBeNull();
    }

    [Theory, AutoData]
    public void SnapshotContainsAllCreatedBranches(string branchName)
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.AddFileToRepository();
        repository.CommitChanges();
        repository.AddBranch(branchName);

        var snapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);

        snapshot.Branches.FirstOrDefault(x => x.Name == branchName).Should().NotBeNull();
    }

    [Theory, AutoData]
    public void HeadContainsMaster(string branchName)
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.AddFileToRepository();
        repository.CommitChanges();
        repository.AddBranch(branchName);

        var snapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);

        snapshot.Head.Should().Be("master");
    }

    [Theory, AutoData]
    public void HeadContainsCheckoutBranch(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.AddFileToRepository();
        repo.CommitChanges();
        repo.AddBranch(branchName);
        repo.CheckoutBranch(branchName);

        var snapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        snapshot.Head.Should().Be(branchName);
    }

    [Fact]
    public void BranchesContainsCommits()
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.AddFileToRepository();
        repository.CommitChanges();

        var snapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);

        var commit = snapshot.Branches.First(x => x.Name == "master").Tip;

        repository.IsValidCommit(commit).Should().BeTrue();
    }

    [Theory, AutoData]
    public void SnapshotContainsRemotes(string remoteName)
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

        snapshot.Remotes.First(x => x.Name == remoteName).Url.Should().Be(remoteUrl);
    }

    [Theory, AutoData]
    public void SnapshotContainsRemoteBranches(string remoteName)
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

        snapshot.Remotes.First(x => x.Name == remoteName)
            .Branches
            .Should()
            .ContainSingle()
            .Which
            .Key
            .Should()
            .Be($"{remoteName}/master");
    }

    [Theory, AutoData]
    public void SnapshotDoesNotContainRemoteBranches(string remoteName)
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

        snapshot.Branches.Should()
            .ContainSingle()
            .Which
            .Name
            .Should()
            .Be("master");
    }

    [Theory, AutoData]
    public void RemoteTipIsValidSha(string remoteName)
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

        using (new AssertionScope())
        {
            original.IsValidCommit(commit).Should().BeTrue();
            remote.IsValidCommit(commit).Should().BeTrue();
        }
    }

    [Theory, AutoData]
    public void BranchShowsFollowedBranch(string remoteName, string branchName)
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

        using (new AssertionScope())
        {
            var branch = snapshot.Branches.First(x => x.Name == "master");

            branch.TrackedBranch.Should().Be($"{remoteName}/{branchName}");
            branch.RemoteName.Should().Be(remoteName);
        }
    }

    [Theory, AutoData]
    public void SnapshotContainsTags(string tagName)
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.AddFileToRepository();
        repository.CommitChanges();
        repository.Tags.Add(tagName, repository.Head.Tip);

        var snapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);

        var tag = snapshot.Tags.First(x => x.Name == tagName);
        repository.IsValidCommit(tag.Target).Should().BeTrue();
    }

    [Theory, AutoData]
    public void AnnotatedTagContainsMessage(string tagName, string tagMessage)
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.AddFileToRepository();
        repository.CommitChanges();
        var signature = repository.Config.BuildSignature(DateTimeOffset.Now);
        repository.Tags.Add(tagName, repository.Head.Tip, signature, tagMessage);

        var snapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);

        snapshot.Tags.First(x => x.Name == tagName).Message!.TrimEnd().Should().Be(tagMessage);
    }

    [Theory, AutoData]
    public void NonAnnotatedTagContainsNullForMessage(string tagName)
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.AddFileToRepository();
        repository.CommitChanges();
        repository.Tags.Add(tagName, repository.Head.Tip);

        var snapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);

        snapshot.Tags.First(x => x.Name == tagName).Message.Should().BeNull();
    }
}