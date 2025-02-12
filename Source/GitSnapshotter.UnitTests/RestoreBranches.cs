using FluentAssertions;

using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

public sealed class RestoreBranches
{
    [Theory, AutoData]
    public void RestoreBranch(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.AddFileToRepository();
        repo.CommitChanges();
        repo.AddBranch(branchName);
        var originalSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        repo.Branches.Remove(branchName);

        GitRepository.Restore(repo.Info.WorkingDirectory, originalSnapshot);
        var newSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        newSnapshot.Should().BeEquivalentTo(originalSnapshot);
    }

    [Theory, AutoData]
    public void RestoreTwoBranches(string branchName1, string branchName2)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.AddFileToRepository();
        repo.CommitChanges();
        repo.AddBranch(branchName1);
        repo.AddBranch(branchName2);
        var originalSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        repo.Branches.Remove(branchName1);
        repo.Branches.Remove(branchName2);

        GitRepository.Restore(repo.Info.WorkingDirectory, originalSnapshot);
        var newSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        newSnapshot.Should().BeEquivalentTo(originalSnapshot);
    }

    [Theory, AutoData]
    public void RestoreMaster(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.AddFileToRepository();
        repo.CommitChanges();
        repo.AddBranch(branchName);
        Commands.Checkout(repo, branchName);

        var originalSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        repo.Branches.Remove("master");

        GitRepository.Restore(repo.Info.WorkingDirectory, originalSnapshot);
        var newSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        newSnapshot.Should().BeEquivalentTo(originalSnapshot);
    }

    [Theory, AutoData]
    public void RestoreBranchThatIsNotAtTop(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.AddFileToRepository();
        repo.CommitChanges();
        repo.AddBranch(branchName);
        repo.AddFileToRepository();
        repo.CommitChanges();
        var originalSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        repo.Branches.Remove(branchName);

        GitRepository.Restore(repo.Info.WorkingDirectory, originalSnapshot);
        var newSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        newSnapshot.Should().BeEquivalentTo(originalSnapshot);
    }

    [Fact]
    public void RestoreIsIdempotent()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.AddFileToRepository();
        repo.CommitChanges();
        var originalSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        GitRepository.Restore(repo.Info.WorkingDirectory, originalSnapshot);
        var newSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        newSnapshot.Should().BeEquivalentTo(originalSnapshot);
    }
    
    [Theory, AutoData]
    public void RestoreTip(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.AddFileToRepository();
        repo.CommitChanges();
        repo.AddBranch(branchName);
        var originalSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);
        repo.AddFileToRepository();
        repo.CommitChanges();
        repo.Refs.UpdateTarget(repo.Branches[branchName].Reference, repo.Head.Tip.Sha);

        GitRepository.Restore(repo.Info.WorkingDirectory, originalSnapshot);
        var newSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);
        
        newSnapshot.Should().BeEquivalentTo(originalSnapshot);
    }
}