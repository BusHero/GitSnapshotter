using FluentAssertions;

using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

public sealed class RestoreTagsTests
{
    [Theory, AutoData]
    public void RemovedTagIsAddedBack(string tagName)
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.AddFileToRepository();
        repository.CommitChanges();
        repository.Tags.Add(tagName, repository.Head.Tip);
        var originalSnapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);
        repository.Tags.Remove(tagName);

        GitRepository.Restore(repository.Info.WorkingDirectory, originalSnapshot);

        var newSnapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);
        newSnapshot.Should().BeEquivalentTo(originalSnapshot);
    }
    
    [Theory, AutoData]
    public void RemovedAnnotatedTagIsAddedBack(string tagName, string message)
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.AddFileToRepository();
        repository.CommitChanges();
        var signature = repository.Config.BuildSignature(DateTimeOffset.Now);
        repository.Tags.Add(tagName, repository.Head.Tip, signature, message);
        var originalSnapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);
        repository.Tags.Remove(tagName);

        GitRepository.Restore(repository.Info.WorkingDirectory, originalSnapshot);

        var newSnapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);
        newSnapshot.Should().BeEquivalentTo(originalSnapshot);
    }

    [Theory, AutoData]
    public void AddedTagIsRemoved(string tagName)
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.AddFileToRepository();
        repository.CommitChanges();
        var originalSnapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);
        repository.Tags.Add(tagName, repository.Head.Tip);

        GitRepository.Restore(repository.Info.WorkingDirectory, originalSnapshot);

        var newSnapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);
        newSnapshot.Should().BeEquivalentTo(originalSnapshot);
    }

    [Theory, AutoData]
    public void RestoreTagReference(string tagName)
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.AddFileToRepository();
        repository.CommitChanges();
        repository.Tags.Add(tagName, repository.Head.Tip);
        repository.AddFileToRepository();
        repository.CommitChanges();
        var originalSnapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);
        repository.Refs.UpdateTarget(repository.Tags[tagName].Reference, repository.Head.Tip.Sha);
        
        GitRepository.Restore(repository.Info.WorkingDirectory, originalSnapshot);

        var newSnapshot = GitRepository.GetSnapshot(repository.Info.WorkingDirectory);
        newSnapshot.Should().BeEquivalentTo(originalSnapshot);
    }
}