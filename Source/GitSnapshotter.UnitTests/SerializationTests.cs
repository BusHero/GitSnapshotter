using System.Text.Json;

using FluentAssertions;
using FluentAssertions.Execution;

namespace GitSnapshotter.UnitTests;

public sealed class SerializationTests
{
    [Test, AutoData]
    public void JsonRoundTripKeepsEverything(string tagName, string message)
    {
        var (clone, origin) = GitTasks.CreateClonedRepository();
        using var _ = clone;
        using var __ = origin;
        clone.AddTag(tagName);
        clone.AddAnnotatedTag(tagName + "-annotated", message);
        clone.Refs.Add("refs/custom/marker", clone.Head.Tip.Id);
        clone.WriteFile("wip.txt", "wip");
        var snapshot = clone.SnapshotWithWorkingTree();

        var copy = GitRepositorySnapshot.FromJson(snapshot.ToJson());

        copy.Should().BeEquivalentTo(snapshot);
    }

    [Test]
    public void SaveAndLoadUseAFile()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();
        var file = GitTasks.GetTempPath() + ".json";

        snapshot.Save(file);

        using (new AssertionScope())
        {
            File.Exists(file).Should().BeTrue();
            GitRepositorySnapshot.Load(file).Should().BeEquivalentTo(snapshot);
        }
    }

    [Test]
    public void LoadedSnapshotCanBeRestored()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var file = GitTasks.GetTempPath() + ".json";
        repo.Snapshot().Save(file);

        repo.AddBranch("feature");
        repo.AddTag("v1");
        repo.RestoreFrom(GitRepositorySnapshot.Load(file));

        repo.Reopen().Snapshot().Should().BeEquivalentTo(GitRepositorySnapshot.Load(file));
    }

    [Test]
    public void SnapshotFromEarlierVersionCanBeRead()
    {
        // The format written by version 0.0.5's console app.
        const string json = """
            {
              "Head": "master",
              "Branches": [
                { "Name": "master", "Tip": "0123456789abcdef0123456789abcdef01234567", "TrackedBranch": null, "RemoteName": null }
              ],
              "Remotes": [],
              "Tags": [
                { "Name": "v1", "Target": "0123456789abcdef0123456789abcdef01234567", "Message": null }
              ]
            }
            """;

        var snapshot = GitRepositorySnapshot.FromJson(json);

        using (new AssertionScope())
        {
            snapshot.Head.Should().Be("master");
            snapshot.IsHeadDetached.Should().BeFalse();
            snapshot.Branches.Should().ContainSingle().Which.Name.Should().Be("master");
            snapshot.Tags.Should().ContainSingle().Which.AnnotationSha.Should().BeNull();
            snapshot.References.Should().BeEmpty();
            snapshot.WorkingTree.Should().BeNull();
        }
    }

    [Test]
    public void JsonWithoutRequiredFieldsIsRejected()
    {
        var read = () => GitRepositorySnapshot.FromJson("""{ "Head": "master" }""");

        read.Should().Throw<JsonException>();
    }

    [Test]
    public void NullJsonIsRejected()
    {
        var read = () => GitRepositorySnapshot.FromJson("null");

        read.Should().Throw<JsonException>();
    }
}
