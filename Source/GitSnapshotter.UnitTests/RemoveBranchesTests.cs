namespace GitSnapshotter.UnitTests;

public sealed class RemoveBranchesTests
{
    [Test, AutoData]
    public async Task RemoveNewlyAddedBranch(string branchName)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.AddFileToRepository();
        repo.CommitChanges();
        var originalSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        repo.AddBranch(branchName);

        GitRepository.Restore(repo.Info.WorkingDirectory, originalSnapshot);
        var newSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        await Assert.That(newSnapshot).IsEquivalentTo(originalSnapshot);
    }

    [Test, AutoData]
    public async Task RemoveSeveralBranches(string branchName1, string branchName2)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.AddFileToRepository();
        repo.CommitChanges();
        var originalSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        repo.AddBranch(branchName1);
        repo.AddBranch(branchName2);

        GitRepository.Restore(repo.Info.WorkingDirectory, originalSnapshot);
        var newSnapshot = GitRepository.GetSnapshot(repo.Info.WorkingDirectory);

        await Assert.That(newSnapshot).IsEquivalentTo(originalSnapshot);
    }
}
