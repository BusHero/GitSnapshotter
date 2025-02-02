using FluentAssertions;
using FluentAssertions.Execution;

using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

public class SnapshotTests
{
    [Fact]
    public void SnapshotOfInitialRepositoryContainsDefaultBranch()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.AddFileToRepository();

        repo.CommitChanges();

        var snapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        snapshot.Branches.Should().ContainKey("master");
    }

    [Theory, AutoData]
    public void SnapshotContainsAllCreatedBranches(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.AddFileToRepository();
        repo.CommitChanges();
        repo.AddBranch(branchName);

        var snapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        snapshot.Branches.Should().ContainKey(branchName);
    }

    [Theory, AutoData]
    public void HeadContainsMaster(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.AddFileToRepository();
        repo.CommitChanges();
        repo.AddBranch(branchName);

        var snapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

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
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.AddFileToRepository();
        repo.CommitChanges();

        var snapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        var commit = snapshot.Branches["master"].Tip;

        repo.IsValidCommit(commit).Should().BeTrue();
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

        snapshot.Remotes[remoteName].Url.Should().Be(remoteUrl);
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

        snapshot.Remotes[remoteName]
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
            .Key
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

        var commit = snapshot.Remotes[remoteName]
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
            snapshot.Branches["master"].IsTracking.Should().BeTrue();
            snapshot.Branches["master"].TrackedBranch.Should().Be($"{remoteName}/{branchName}");
            snapshot.Branches["master"].RemoteName.Should().Be(remoteName);
        }
    }
}