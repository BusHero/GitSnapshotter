using System.Text.Json;


namespace GitSnapshotter.UnitTests;

public sealed class SerializationTests
{
    [Test, AutoData]
    public async Task JsonRoundTripKeepsEverything(string tagName, string message)
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

        await SnapshotAssert.AreEquivalent(copy, snapshot);
    }

    [Test]
    [Arguments("git@github.com:owner/repo.git")]
    [Arguments("https://github.com/owner/repo.git")]
    [Arguments("ssh://git@example.com:2222/repo.git")]
    [Arguments(@"C:\repos\origin")]
    [Arguments("../origin")]
    public async Task RemoteUrlRoundTripsUnchanged(string url)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.Network.Remotes.Add("origin", url);
        var snapshot = repo.Snapshot();

        var copy = GitRepositorySnapshot.FromJson(snapshot.ToJson());

        using (Assert.Multiple())
        {
            await Assert.That(snapshot.Remotes[0].Url.OriginalString).IsEqualTo(url);
            await Assert.That(copy.Remotes[0].Url.OriginalString).IsEqualTo(url);
        }
    }

    [Test]
    public async Task SaveAndLoadUseAFile()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var snapshot = repo.Snapshot();
        var file = GitTasks.GetTempPath() + ".json";

        snapshot.Save(file);

        using (Assert.Multiple())
        {
            await Assert.That(File.Exists(file)).IsTrue();
            await SnapshotAssert.AreEquivalent(GitRepositorySnapshot.Load(file), snapshot);
        }
    }

    [Test]
    public async Task LoadedSnapshotCanBeRestored()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var file = GitTasks.GetTempPath() + ".json";
        repo.Snapshot().Save(file);

        repo.AddBranch("feature");
        repo.AddTag("v1");
        repo.RestoreFrom(GitRepositorySnapshot.Load(file));

        using var reopened = repo.Reopen();
        await SnapshotAssert.AreEquivalent(reopened.Snapshot(), GitRepositorySnapshot.Load(file));
    }

    [Test]
    public async Task SnapshotFromEarlierVersionCanBeRead()
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

        using (Assert.Multiple())
        {
            await Assert.That(snapshot.Head).IsEqualTo("master");
            await Assert.That(snapshot.IsHeadDetached).IsFalse();
            var item = await Assert.That(snapshot.Branches).HasSingleItem();
            await Assert.That(item.Name).IsEqualTo("master");
            var item2 = await Assert.That(snapshot.Tags).HasSingleItem();
            await Assert.That(item2.AnnotationSha).IsNull();
            await Assert.That(snapshot.References).IsEmpty();
            await Assert.That(snapshot.WorkingTree).IsNull();
        }
    }

    [Test]
    public async Task JsonWithoutRequiredFieldsIsRejected()
    {
        var read = () => GitRepositorySnapshot.FromJson("""{ "Head": "master" }""");

        await Assert.That(read).Throws<JsonException>();
    }

    [Test]
    public async Task NullJsonIsRejected()
    {
        var read = () => GitRepositorySnapshot.FromJson("null");

        await Assert.That(read).Throws<JsonException>();
    }
}
