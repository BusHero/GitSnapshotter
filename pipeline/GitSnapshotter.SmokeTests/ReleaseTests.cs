using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using GitSnapshotter.Pipeline;

using TUnit.Core;

using static GitSnapshotter.SmokeTests.SmokeTestChecks;

namespace GitSnapshotter.SmokeTests;

/// <summary>The GitHub release, its assets, and the published docs.</summary>
[ClassDataSource<PublishedRelease>(Shared = SharedType.PerTestSession)]
public sealed class ReleaseTests(PublishedRelease release)
{
    [Test]
    public async Task ReleaseAssetsMatchTheirChecksums()
    {
        var directory = ReleaseDirectory();

        await Assert.That(() => VerifyChecksums(directory, ChecksumsFile)).ThrowsNothing();
    }

    [Test]
    [Arguments(ToolId)]
    [Arguments(LibraryId)]
    public async Task FeedsPackageIsTheReleases(string id)
    {
        var directory = ReleaseDirectory();
        var package = id == ToolId ? release.ToolPackage : release.LibraryPackage;

        await Assert.That(() => ComparePackages(package, Path.Combine(directory, $"{id}.{release.Version}.nupkg"))).ThrowsNothing();
    }

    [Test]
    public async Task ConsoleAppZipHasItsNoticesAndRuns()
    {
        var directory = ReleaseDirectory();
        var zip = Path.Combine(directory, $"GitSnapshotter.Console_{release.Version}.zip");
        var console = Path.Combine(release.Work, "console");
        await ZipFile.ExtractToDirectoryAsync(zip, console);

        var notices = Path.Combine(console, ThirdPartyNotices.FileName);
        await Assert.That(File.Exists(notices)).IsTrue();
        CheckNotices(await File.ReadAllTextAsync(notices), ThirdPartyNotices.ConsoleProduct, Path.GetFileName(zip));

        // Through dotnet, so it runs on every OS: the zip's app host is built for Linux.
        var dll = Path.Combine(console, ConsoleAssembly);
        var output = (await release.DotnetAsync([dll, "--version"])).StandardOutput;
        await Assert.That(() => CheckToolVersion(output, release.Version)).ThrowsNothing();
        await release.DotnetAsync([dll, "snapshot", release.Settings.Root]);
    }

    [Test]
    public async Task GitHubReleaseIsPublishedFlaggedTaggedDescribedAndComplete()
    {
        Skip.Unless(release.Settings.GitHubRelease, "The GitHub release is not checked");

        var version = ReleaseVersion.Parse(release.Version);
        var repository = release.Settings.GitHubRepository;
        using var published = await GitHubAsync($"repos/{repository}/releases/tags/{version.Tag}");
        using var latest = await GitHubAsync($"repos/{repository}/releases/latest");
        var tagCommit = await TagCommitAsync(repository, version.Tag);
        var (_, notes) = Changelog.ReleaseNotes(
            await File.ReadAllLinesAsync(Path.Combine(release.Settings.Root, Changelog.FileName)), version);

        await Assert.That(published).IsNotNull().Because($"{repository} must have a published release {version.Tag}");
        var root = published!.RootElement;
        var actual = new GitHubRelease(
            root.GetProperty("draft").GetBoolean(),
            root.GetProperty("prerelease").GetBoolean(),
            root.GetProperty("body").GetString(),
            [.. root.GetProperty("assets").EnumerateArray().Select(asset => asset.GetProperty("name").GetString()!)],
            tagCommit,
            latest?.RootElement.GetProperty("tag_name").GetString());

        await Assert.That(CheckGitHubRelease(actual, version, release.Commit, notes)).IsEmpty();
    }

    [Test]
    public async Task InstallationPageMatchesTheRelease()
    {
        Skip.Unless(release.Settings.Docs, "The docs are not checked");

        var page = PageText(await release.Http.GetStringAsync(new Uri(release.Settings.DocsPage)));

        await Assert.That(CheckDocs(page, LibFrameworks(release.LibraryEntries.Keys), ToolFrameworks(release.ToolEntries.Keys))).IsEmpty();
    }

    private string ReleaseDirectory()
    {
        Skip.When(release.Release is null, "No release directory (SmokeTest__ReleaseDirectory)");
        return release.Release!;
    }

    // The commit a tag points at, through an annotated tag if need be; none when there is no such tag.
    private async Task<string?> TagCommitAsync(string repository, string tag)
    {
        using var reference = await GitHubAsync($"repos/{repository}/git/ref/tags/{tag}");
        if (reference is null)
        {
            return null;
        }

        var target = reference.RootElement.GetProperty("object");
        if (target.GetProperty("type").GetString() != "tag")
        {
            return target.GetProperty("sha").GetString();
        }

        using var annotated = await GitHubAsync($"repos/{repository}/git/tags/{target.GetProperty("sha").GetString()}");
        return annotated?.RootElement.GetProperty("object").GetProperty("sha").GetString();
    }

    // A GitHub REST API response; none on 404. Authenticated with GITHUB_TOKEN when it is set.
    private async Task<JsonDocument?> GitHubAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://api.github.com/{path}"));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        if (Environment.GetEnvironmentVariable("GITHUB_TOKEN") is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await release.Http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
