using ModularPipelines.Attributes;
using ModularPipelines.Models;

namespace GitSnapshotter.Pipeline;

/// <summary>
/// Pipeline settings, bound from the <c>Build</c> configuration section:
/// environment variables like <c>Build__PublishDirectory</c>, or arguments like <c>--Build:PublishDirectory=out</c>.
/// </summary>
public sealed class BuildSettings
{
    public const string Section = "Build";

    /// <summary>Which modules run. The whole build by default.</summary>
    public BuildTarget Target { get; set; } = BuildTarget.All;

    /// <summary>The build configuration.</summary>
    public string Configuration { get; set; } = "Release";

    /// <summary>Where the NuGet package and the published console app go, relative to the repository root.</summary>
    public string PublishDirectory { get; set; } = "publish";

    /// <summary>
    /// Where a release build puts the files attached to the GitHub release, relative to the repository root.
    /// </summary>
    public string ReleaseDirectory { get; set; } = "release";

    /// <summary>
    /// Where <see cref="BuildTarget.VerifyRelease"/> writes the release description, relative to the repository root.
    /// </summary>
    public string ReleaseNotesDirectory { get; set; } = "release-notes";

    /// <summary>
    /// The GitHub environment that publishes releases. <see cref="BuildTarget.VerifyRelease"/> fails unless it
    /// requires a reviewer, so no release goes out without a person approving it.
    /// </summary>
    public string ReleaseEnvironment { get; set; } = "nuget";

    /// <summary>The repository's default branch. Detected from git when not set.</summary>
    public string? DefaultBranch { get; set; }

    /// <summary>The feed the package is pushed to.</summary>
    public string NuGetSource { get; set; } = "https://nuget.pkg.github.com/BusHero/index.json";

    /// <summary>The API key for <see cref="NuGetSource"/>. When empty, the package is not pushed.</summary>
    [SecretValue]
    public string? NuGetApiKey { get; set; }

    /// <summary>The feed <see cref="BuildTarget.SmokeTest"/> downloads the published packages from.</summary>
    public string SmokeTestSource { get; set; } = "https://api.nuget.org/v3/index.json";

    /// <summary>
    /// How long <see cref="BuildTarget.SmokeTest"/> keeps retrying while <see cref="SmokeTestSource"/> doesn't list
    /// the packages yet. nuget.org validates and indexes a package for several minutes after it is pushed.
    /// </summary>
    public int SmokeTestWaitMinutes { get; set; } = 30;

    /// <summary>
    /// A directory holding the GitHub release's assets, relative to the repository root. When set,
    /// <see cref="BuildTarget.SmokeTest"/> also checks that the packages on <see cref="SmokeTestSource"/> are the ones
    /// attached to the release.
    /// </summary>
    public string? SmokeTestReleaseDirectory { get; set; }

    /// <summary>
    /// The symbol server <see cref="BuildTarget.SmokeTest"/> downloads our assemblies' PDBs from, in the SSQP format
    /// (<c>&lt;server&gt;/&lt;pdb&gt;/&lt;id&gt;/&lt;pdb&gt;</c>). When empty, the symbols are not checked.
    /// </summary>
    public string SmokeTestSymbolServer { get; set; } = "https://symbols.nuget.org/download/symbols";

    /// <summary>
    /// Whether <see cref="BuildTarget.SmokeTest"/> downloads the PDBs from <see cref="SmokeTestSymbolServer"/>. A
    /// setting of its own, not an empty server: on Windows an empty environment variable is no variable at all.
    /// </summary>
    public bool SmokeTestSymbols { get; set; } = true;

    /// <summary>
    /// The commit the release was made from. When set, <see cref="BuildTarget.SmokeTest"/> checks that both packages
    /// were built from it (the commit in their nuspec, which the feed shows).
    /// </summary>
    public string? SmokeTestCommit { get; set; }

    /// <summary>
    /// Whether <see cref="BuildTarget.SmokeTest"/> verifies the packages' signatures (<c>dotnet nuget verify</c>).
    /// nuget.org signs every package; a feed that doesn't, like GitHub Packages, needs this off.
    /// </summary>
    public bool SmokeTestVerifySignatures { get; set; } = true;

    /// <summary>
    /// Whether <see cref="BuildTarget.SmokeTest"/> checks the GitHub release: published, flagged right, tagged on the
    /// commit, described by CHANGELOG.md and with the release assets. Reads the repository with <c>GITHUB_TOKEN</c>.
    /// </summary>
    public bool SmokeTestGitHubRelease { get; set; } = true;

    /// <summary>
    /// Whether <see cref="BuildTarget.SmokeTest"/> checks what the feed shows about the packages (metadata, readme,
    /// icon), with nuget.org's registration API. Off for a feed without it, like GitHub Packages.
    /// </summary>
    public bool SmokeTestFeedMetadata { get; set; } = true;

    /// <summary>
    /// Where <see cref="BuildTarget.SmokeTest"/> gets the previous releases (to upgrade from, restore old snapshots,
    /// compare the API and the package size with) and every package ours depend on.
    /// </summary>
    public string SmokeTestPreviousSource { get; set; } = SmokeTestChecks.NuGetOrg;

    /// <summary>The user name for <see cref="SmokeTestSource"/>, when it needs one (GitHub Packages).</summary>
    public string? SmokeTestSourceUsername { get; set; }

    /// <summary>The password or token for <see cref="SmokeTestSource"/>, when it needs one (GitHub Packages).</summary>
    [SecretValue]
    public string? SmokeTestSourcePassword { get; set; }

    /// <summary>
    /// The published installation page <see cref="BuildTarget.SmokeTest"/> checks against the release. When empty, the
    /// docs are not checked.
    /// </summary>
    public string? SmokeTestDocsPage { get; set; } = "https://bushero.github.io/GitSnapshotter/installation/";

    /// <summary>Whether <see cref="BuildTarget.SmokeTest"/> checks <see cref="SmokeTestDocsPage"/>.</summary>
    public bool SmokeTestDocs { get; set; } = true;

    /// <summary>
    /// How much (in percent) each framework's part of a package may grow over the previous release before
    /// <see cref="BuildTarget.SmokeTest"/> fails.
    /// </summary>
    public int SmokeTestMaxGrowthPercent { get; set; } = 25;

    /// <summary>
    /// Whether <see cref="BuildTarget.SmokeTest"/> fails when the version sorts below a stable release on
    /// <see cref="SmokeTestPreviousSource"/>. On for CI builds, which are versioned after the <c>VersionPrefix</c> in
    /// Source/Directory.Build.props: it must be raised after each stable release. Off for releases, as a fix for an
    /// older line (a 0.3.1 after 0.4.0) may sort below.
    /// </summary>
    public bool SmokeTestAboveReleases { get; set; }

    /// <summary>
    /// The release version, like <c>1.2.0</c> or <c>1.2.0-rc.1</c>. When set, the assemblies, the package and the
    /// console app get exactly this version; otherwise the package is a prerelease named after the branch. A leading
    /// <c>v</c>, as in a tag name, is dropped.
    /// </summary>
    public string? Version
    {
        get;
        set => field = string.IsNullOrWhiteSpace(value) ? null : ReleaseVersion.Normalize(value.Trim());
    }

    /// <summary>Whether this is a release build (<see cref="Version"/> is set).</summary>
    internal bool IsRelease => !string.IsNullOrEmpty(Version);

    /// <summary>The MSBuild properties that stamp <see cref="Version"/> on a release build; none otherwise.</summary>
    internal KeyValue[] VersionProperties => IsRelease ? [new KeyValue("Version", Version!)] : [];
}
