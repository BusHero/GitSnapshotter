using TUnit.Core;

using static GitSnapshotter.SmokeTests.SmokeTestChecks;

namespace GitSnapshotter.SmokeTests;

/// <summary>The .NET tool, used the ways users use it.</summary>
[ClassDataSource<PublishedRelease>(Shared = SharedType.PerTestSession)]
public sealed class ToolTests(PublishedRelease release)
{
    [Test]
    public async Task InstalledToolPrintsItsVersion()
    {
        var output = (await release.RunAsync(release.Tool, "--version")).StandardOutput;

        await Assert.That(() => CheckToolVersion(output, release.Version)).ThrowsNothing();
    }

    [Test]
    public async Task InstalledToolSnapshotsThisRepository()
    {
        var output = (await release.RunAsync(release.Tool, "snapshot", release.Settings.Root)).StandardOutput;

        await Assert.That(output).Contains("\"Branches\"");
    }

    [Test]
    public async Task EveryFrameworkBuildInThePackageRuns()
    {
        // dotnet tool install runs the build that best matches the newest runtime; a machine with an older one runs
        // another.
        var frameworks = ToolFrameworks(release.ToolEntries.Keys);
        await Assert.That(frameworks).IsNotEmpty();

        foreach (var framework in frameworks)
        {
            var build = Path.Combine(Path.GetDirectoryName(release.ToolPackage)!, "tools", framework, "any", ConsoleAssembly);
            var output = (await release.DotnetAsync([build, "--version"])).StandardOutput;
            await Assert.That(() => CheckToolVersion(output, release.Version)).ThrowsNothing();
            await release.DotnetAsync([build, "snapshot", release.Settings.Root]);
        }
    }

    [Test]
    public async Task RestoresARepositoryAndItsWorkingTreeToASnapshot()
    {
        var repo = await release.CreateRepositoryAsync("round-trip");
        var before = await SnapshotAsync(release.Tool, repo, "round-trip-before");

        await ChangeAndRestoreAsync(repo, before.Path, before.Json);
    }

    [Test]
    public async Task RestoresASnapshotThePreviousReleaseTook()
    {
        Skip.When(release.PreviousTool is null, $"No release of {ToolId} before {release.Version}");

        var previous = await release.InstallPreviousToolAsync(Path.Combine(release.Work, "previous-tool"));
        var repo = await release.CreateRepositoryAsync("old-snapshot");
        var before = await SnapshotAsync(release.Tool, repo, "old-snapshot-before");
        var old = await SnapshotAsync(previous, repo, "old-snapshot-previous");

        await ChangeAndRestoreAsync(repo, old.Path, before.Json);
    }

    [Test]
    public async Task UpgradesFromThePreviousReleaseAndUninstalls()
    {
        Skip.When(release.PreviousTool is null, $"No release of {ToolId} before {release.Version}");

        var upgrade = Path.Combine(release.Work, "upgrade");
        var tool = await release.InstallPreviousToolAsync(upgrade);
        await release.DotnetAsync(["tool", "update", ToolId, "--version", release.Version, "--tool-path", upgrade, "--configfile", release.NuGetConfig]);
        var output = (await release.RunAsync(tool, "--version")).StandardOutput;
        await Assert.That(() => CheckToolVersion(output, release.Version)).ThrowsNothing();

        await release.DotnetAsync(["tool", "uninstall", ToolId, "--tool-path", upgrade]);
        await Assert.That(File.Exists(tool)).IsFalse();
    }

    [Test]
    public async Task RestoresAndRunsAsALocalTool()
    {
        // As a team uses it: the version pinned in .config/dotnet-tools.json, restored after a clone.
        var local = Path.Combine(release.Work, "local");
        Directory.CreateDirectory(Path.Combine(local, ".config"));
        await PublishedRelease.WriteAsync(Path.Combine(local, ".config", "dotnet-tools.json"), ToolManifest(release.Version));

        await release.DotnetAsync(["tool", "restore", "--configfile", release.NuGetConfig], local);
        foreach (var command in new[] { new[] { "tool", "run", ToolCommand, "--version" }, [ToolCommand, "--version"] })
        {
            var output = (await release.DotnetAsync(command, local)).StandardOutput;
            await Assert.That(() => CheckToolVersion(output, release.Version)).ThrowsNothing();
        }
    }

    [Test]
    public async Task RunsWithDotnetToolExec()
    {
        // dnx runs this: the tool without installing it.
        var output = (await release.DotnetAsync(
            ["tool", "exec", $"{ToolId}@{release.Version}", "--configfile", release.NuGetConfig, "--", "--version"])).StandardOutput;

        await Assert.That(() => CheckToolVersion(output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1], release.Version)).ThrowsNothing();
    }

    private async Task<(string Path, string Json)> SnapshotAsync(string tool, string repo, string name)
    {
        var path = Path.Combine(release.Work, $"{name}.json");
        await release.RunAsync(tool, "snapshot", repo, "--output", path, "--include-working-tree");
        return (path, await File.ReadAllTextAsync(path));
    }

    // Commits everything (so the restore loses nothing), checks out a new branch with a commit, moves the tag and
    // deletes a branch; then restores the snapshot and checks the repository is back as it was.
    private async Task ChangeAndRestoreAsync(string repo, string snapshot, string expected)
    {
        var file = Path.Combine(repo, "file.txt");
        await release.GitAsync(repo, "add", "--all");
        await release.GitAsync(repo, "commit", "--message", "two");
        await release.GitAsync(repo, "checkout", "--quiet", "-b", "extra");
        await File.WriteAllTextAsync(file, "three\n");
        await release.GitAsync(repo, "commit", "--all", "--message", "three");
        await release.GitAsync(repo, "tag", "--force", "v1");
        await release.GitAsync(repo, "branch", "--delete", "--force", "feature");
        var changed = await SnapshotAsync(release.Tool, repo, $"{Path.GetFileName(repo)}-changed");
        await Assert.That(SameSnapshot(expected, changed.Json)).IsFalse().Because("the snapshot must see the changes");

        await release.RunAsync(release.Tool, "restore", repo, snapshot);

        var after = await SnapshotAsync(release.Tool, repo, $"{Path.GetFileName(repo)}-after");
        var staged = (await release.GitAsync(repo, "diff", "--cached", "--name-only")).StandardOutput.Trim();
        var status = (await release.GitAsync(repo, "status", "--porcelain", "--untracked-files=all")).StandardOutput;
        using (Assert.Multiple())
        {
            await Assert.That(SameSnapshot(expected, after.Json)).IsTrue().Because("restore must put the repository back as it was");
            await Assert.That(await File.ReadAllTextAsync(file)).IsEqualTo("one, modified\n");
            await Assert.That(staged).IsEqualTo("staged.txt");
            await Assert.That(status).Contains("?? untracked.txt");
        }
    }
}
