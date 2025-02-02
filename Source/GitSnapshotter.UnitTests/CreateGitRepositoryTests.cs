using FluentAssertions;

namespace GitSnapshotter.UnitTests;

public class CreateGitRepositoryTests
{
    [Fact]
    public void SnapshotOfInitialRepositoryContainsDefaultBranch()
    {
        var pathToRepository = GitTasks.CreateTemporaryGitRepository();
        GitTasks.AddFileToRepository(pathToRepository);
        GitTasks.CommitChanges(pathToRepository);

        var snapshot = GitRepository.GetSnapshot(pathToRepository);

        snapshot.Branches.Should().Contain("master");
    }

    [Theory, AutoData]
    public void SnapshotContainsAllCreatedBranches(string branchName)
    {
        var pathToRepository = GitTasks.CreateTemporaryGitRepository();
        GitTasks.AddFileToRepository(pathToRepository);
        GitTasks.CommitChanges(pathToRepository);
        GitTasks.AddBranch(pathToRepository, branchName);

        var snapshot = GitRepository.GetSnapshot(pathToRepository);

        snapshot.Branches.Should().Contain(branchName);
    }

    [Theory, AutoData]
    public void HeadContainsMaster(string branchName)
    {
        var pathToRepository = GitTasks.CreateTemporaryGitRepository();
        GitTasks.AddFileToRepository(pathToRepository);
        GitTasks.CommitChanges(pathToRepository);
        GitTasks.AddBranch(pathToRepository, branchName);

        var snapshot = GitRepository.GetSnapshot(pathToRepository);

        snapshot.Head.Should().Be("master");
    }

    [Theory, AutoData]
    public void HeadContainsCheckoutBranch(string branchName)
    {
        var pathToRepository = GitTasks.CreateTemporaryGitRepository();
        GitTasks.AddFileToRepository(pathToRepository);
        GitTasks.CommitChanges(pathToRepository);
        GitTasks.AddBranch(pathToRepository, branchName);
        GitTasks.CheckoutBranch(pathToRepository, branchName);

        var snapshot = GitRepository.GetSnapshot(pathToRepository);

        snapshot.Head.Should().Be(branchName);
    }
}