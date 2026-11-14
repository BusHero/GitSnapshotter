using FluentAssertions;
using FluentAssertions.Execution;

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

    [Theory, AutoData]
    public void SeveralMovedTagsAreAllRestored(string tag1, string tag2, string tag3)
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        var first = repository.CommitFile("a.txt", "1");
        repository.AddTag(tag1);
        repository.AddTag(tag2);
        repository.AddTag(tag3);
        var second = repository.CommitFile("a.txt", "2");
        var snapshot = repository.Snapshot();

        repository.MoveTag(tag1, second);
        repository.MoveTag(tag2, second);
        repository.MoveTag(tag3, second);
        repository.RestoreFrom(snapshot);

        using (new AssertionScope())
        {
            repository.Tags[tag1].Target.Sha.Should().Be(first.Sha);
            repository.Tags[tag2].Target.Sha.Should().Be(first.Sha);
            repository.Tags[tag3].Target.Sha.Should().Be(first.Sha);
            repository.Snapshot().Should().BeEquivalentTo(snapshot);
        }
    }

    [Theory, AutoData]
    public void OnlyTheMovedTagIsChanged(string unchanged, string moved)
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.CommitFile("a.txt", "1");
        repository.AddTag(unchanged);
        repository.AddTag(moved);
        var second = repository.CommitFile("a.txt", "2");
        var snapshot = repository.Snapshot();

        repository.MoveTag(moved, second);
        repository.RestoreFrom(snapshot);

        repository.Snapshot().Should().BeEquivalentTo(snapshot);
    }

    [Theory, AutoData]
    public void MovedAnnotatedTagGetsItsOriginalAnnotationBack(string tagName, string message)
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.CommitFile("a.txt", "1");
        // An old date, so a recreated annotation (stamped "now") would have a different id.
        var tagger = new Signature("someone", "someone@example.com", new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero));
        repository.Tags.Add(tagName, repository.Head.Tip, tagger, message);
        var originalAnnotation = repository.Tags[tagName].Annotation.Sha;
        var snapshot = repository.Snapshot();

        repository.Tags.Remove(tagName);
        repository.CommitFile("a.txt", "2");
        repository.Tags.Add(tagName, repository.Head.Tip, tagger, "another message");
        repository.RestoreFrom(snapshot);

        using (new AssertionScope())
        {
            repository.Tags[tagName].Annotation.Sha.Should().Be(originalAnnotation);
            repository.Snapshot().Should().BeEquivalentTo(snapshot);
        }
    }

    [Theory, AutoData]
    public void AnnotatedTagIsRecreatedWhenSnapshotHasNoAnnotationId(string tagName, string message)
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        var commit = repository.CommitFile("a.txt", "1");
        repository.AddAnnotatedTag(tagName, message);
        // Snapshots from earlier versions did not record the annotation id.
        var snapshot = repository.Snapshot() with
        {
            Tags = repository.Snapshot().Tags.Select(t => t with { AnnotationSha = null }).ToArray(),
        };

        repository.Tags.Remove(tagName);
        repository.RestoreFrom(snapshot);

        var tag = repository.Tags[tagName];
        using (new AssertionScope())
        {
            tag.IsAnnotated.Should().BeTrue();
            tag.Target.Sha.Should().Be(commit.Sha);
            tag.Annotation.Message.TrimEnd().Should().Be(message);
        }
    }

    [Theory, AutoData]
    public void LightweightTagReplacedByAnnotatedTagIsRestored(string tagName, string message)
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.CommitFile("a.txt", "1");
        repository.AddTag(tagName);
        var snapshot = repository.Snapshot();

        repository.Tags.Remove(tagName);
        repository.AddAnnotatedTag(tagName, message);
        repository.RestoreFrom(snapshot);

        using (new AssertionScope())
        {
            repository.Tags[tagName].IsAnnotated.Should().BeFalse();
            repository.Snapshot().Should().BeEquivalentTo(snapshot);
        }
    }

    [Theory, AutoData]
    public void AnnotatedTagReplacedByLightweightTagIsRestored(string tagName, string message)
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.CommitFile("a.txt", "1");
        repository.AddAnnotatedTag(tagName, message);
        var snapshot = repository.Snapshot();

        repository.Tags.Remove(tagName);
        repository.AddTag(tagName);
        repository.RestoreFrom(snapshot);

        repository.Snapshot().Should().BeEquivalentTo(snapshot);
    }

    [Theory, AutoData]
    public void SnapshotRecordsAnnotationId(string tagName, string message)
    {
        using var repository = GitTasks.CreateTemporaryGitRepository();
        repository.CommitFile("a.txt", "1");
        repository.AddAnnotatedTag(tagName, message);
        repository.AddTag(tagName + "-light");

        var snapshot = repository.Snapshot();

        using (new AssertionScope())
        {
            snapshot.Tags.Single(t => t.Name == tagName).AnnotationSha
                .Should().Be(repository.Tags[tagName].Annotation.Sha);
            snapshot.Tags.Single(t => t.Name == tagName + "-light").AnnotationSha.Should().BeNull();
        }
    }
}
