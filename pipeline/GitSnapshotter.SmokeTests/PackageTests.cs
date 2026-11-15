using System.Xml.Linq;

using GitSnapshotter.Pipeline;

using TUnit.Core;

using static GitSnapshotter.SmokeTests.SmokeTestChecks;

namespace GitSnapshotter.SmokeTests;

/// <summary>What the packages hold, and how they compare with the previous release.</summary>
[ClassDataSource<PublishedRelease>(Shared = SharedType.PerTestSession)]
public sealed class PackageTests(PublishedRelease release)
{
    [Test]
    public async Task ToolPackageHasItsNoticesAndNothingUnexpected()
    {
        var noticed = CheckNotices(release.ToolPackage, ThirdPartyNotices.ConsoleProduct);

        await Assert.That(() => CheckToolContents(release.ToolEntries, noticed)).ThrowsNothing();
    }

    [Test]
    public async Task LibraryPackageHasItsNoticesAndNothingUnexpected()
    {
        CheckNotices(release.LibraryPackage, ThirdPartyNotices.LibraryProduct);
        var project = XDocument.Load(Path.Combine(release.Settings.Root, "Source", "GitSnapshotter", "GitSnapshotter.csproj"));

        await Assert.That(() => CheckLibraryContents(release.LibraryEntries, project)).ThrowsNothing();
    }

    [Test]
    public async Task PackagesAreSigned()
    {
        Skip.Unless(release.Settings.VerifySignatures, "Signatures are not verified (the feed doesn't sign)");

        await release.DotnetAsync(["nuget", "verify", "--all", release.ToolPackage, release.LibraryPackage]);
    }

    [Test]
    public async Task NothingTheLibraryDependsOnHasAKnownVulnerability()
    {
        await AuditAsync("audit-library", new Dictionary<string, string> { [LibraryId] = release.Version });
    }

    [Test]
    public async Task NothingTheToolShipsHasAKnownVulnerability()
    {
        await AuditAsync("audit-tool", CheckNotices(release.ToolPackage, ThirdPartyNotices.ConsoleProduct));
    }

    [Test]
    public async Task LibraryApiHasNoBreakingChangeFromThePreviousRelease()
    {
        Skip.When(release.PreviousLibrary is null, $"No release of {LibraryId} before {release.Version}");

        var previous = await release.DownloadPreviousAsync(LibraryId, release.PreviousLibrary!);
        var apiCompat = Path.Combine(release.Work, "apicompat");
        await release.DotnetAsync(["tool", "install", "Microsoft.DotNet.ApiCompat.Tool", "--tool-path", apiCompat, "--configfile", release.PreviousConfig]);
        var result = await Command.RunAsync(
            Path.Combine(apiCompat, OperatingSystem.IsWindows() ? "apicompat.exe" : "apicompat"),
            ["package", release.LibraryPackage, "--baseline-package", previous],
            release.Work,
            check: false);

        if (result.ExitCode != 0)
        {
            // An intended break is allowed when the version's changelog section owns up to it.
            string? notes;
            try
            {
                (_, notes) = Changelog.ReleaseNotes(
                    await File.ReadAllLinesAsync(Path.Combine(release.Settings.Root, Changelog.FileName)), ReleaseVersion.Parse(release.Version));
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                notes = null;
            }

            await Assert.That(AllowsBreakingChanges(notes)).IsTrue().Because(
                $"{LibraryId} {release.Version} breaks the API of {release.PreviousLibrary}; if that is intended, list the breaking "
                + $"changes in the version's {Changelog.FileName} section, under a heading like '### Breaking changes':\n{result.StandardOutput}");
        }
    }

    [Test]
    [Arguments(ToolId)]
    [Arguments(LibraryId)]
    public async Task PackageDidNotGrowTooMuchSinceThePreviousRelease(string id)
    {
        var (previousVersion, entries) = id == ToolId
            ? (release.PreviousTool, release.ToolEntries)
            : (release.PreviousLibrary, release.LibraryEntries);
        Skip.When(previousVersion is null, $"No release of {id} before {release.Version}");

        var previous = ReadEntries(await release.DownloadPreviousAsync(id, previousVersion!));

        await Assert.That(CheckGrowth(entries, previous, release.Settings.MaxGrowthPercent)).IsEmpty();
    }

    [Test]
    public async Task VersionSortsAboveEveryRelease()
    {
        Skip.Unless(release.Settings.AboveReleases, "Releases may sort below an existing one (a fix for an older line)");

        using (Assert.Multiple())
        {
            foreach (var versions in new[] { release.ToolVersions, release.LibraryVersions })
            {
                await Assert.That(ReleasedAtOrAbove(versions, release.Version)).IsNull().Because(
                    $"{release.Version} must sort above every release, or it could never be released: the build takes the "
                    + "next release after the tagged ones, so check that it was built with every tag (fetch-depth: 0)");
            }
        }
    }

    // Restores a project referencing the packages at their exact versions; NuGet Audit fails it on any known
    // vulnerability, of any severity, in them or anything they depend on, or when it can't read the vulnerability data.
    private async Task AuditAsync(string name, IEnumerable<KeyValuePair<string, string>> packages)
    {
        var directory = Path.Combine(release.Work, name);
        Directory.CreateDirectory(directory);
        await PublishedRelease.WriteAsync(Path.Combine(directory, $"{name}.csproj"), AuditProject(packages, release.CurrentRuntime));
        await release.DotnetAsync(["restore", directory]);
    }
}
