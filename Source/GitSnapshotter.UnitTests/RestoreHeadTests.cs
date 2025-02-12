using FluentAssertions;

using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

public sealed class RestoreHeadTests
{
    [Theory, AutoData]
    public void RestoreHead(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.AddFileToRepository();
        repo.CommitChanges();
        repo.AddBranch(branchName);
        var originalSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);
        Commands.Checkout(repo, branchName);
        
        GitRepository.Restore(repo.Info.WorkingDirectory, originalSnapshot);
        var newSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        newSnapshot.Should().BeEquivalentTo(originalSnapshot);
    }
}