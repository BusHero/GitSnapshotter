using FluentAssertions;

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

        var commit = snapshot.Branches["master"];

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
        GitTasks.AddRemote(original, remoteName, remoteUrl);

        var snapshot = GitRepository.GetSnapshot(original.Info.WorkingDirectory);

        snapshot.Remotes[remoteName].Should().Be(remoteUrl);
    }
}