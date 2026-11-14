using TUnit.Core;

using static GitSnapshotter.SmokeTests.SmokeTestChecks;

namespace GitSnapshotter.SmokeTests;

/// <summary>The library, used as a test would use it.</summary>
[ClassDataSource<PublishedRelease>(Shared = SharedType.PerTestSession)]
public sealed class LibraryTests(PublishedRelease release)
{
    [Test]
    public async Task WorksOnEveryFrameworkItIsBuiltFor()
    {
        // Each framework the package has assemblies for (.NET Framework on Windows only), and the newest runtime.
        var frameworks = TestFrameworks(LibFrameworks(release.LibraryEntries.Keys), release.CurrentRuntime, OperatingSystem.IsWindows());
        await PublishedRelease.WriteAsync(Path.Combine(release.App, "SmokeTest.csproj"), LibraryProject(release.Version, frameworks));
        await release.RetryAsync(
            $"restore the app for {string.Join(", ", frameworks)} with",
            attempt => release.DotnetAsync(["restore", release.App], attempt: attempt));

        var repo = await release.CreateRepositoryAsync("library");
        foreach (var framework in frameworks)
        {
            // Snapshot, Preserve, JSON and Restore; the program throws if the repository isn't put back.
            var output = (await release.DotnetAsync(["run", "--project", release.App, "--framework", framework, "--no-restore", "--", repo])).StandardOutput;
            await Assert.That(output).Contains("snapshot, Preserve and Restore work");
        }
    }
}
