using System.Globalization;

namespace GitSnapshotter.SmokeTests;

/// <summary>
/// What the smoke tests test and how, from <c>SmokeTest__&lt;Name&gt;</c> environment variables (the pipeline's
/// SmokeTest target sets <c>Version</c>, <c>Root</c> and <c>GitHubRepository</c>). Booleans are <c>true</c> or
/// <c>false</c>, never empty: on Windows an empty environment variable is no variable at all.
/// </summary>
public sealed class SmokeTestSettings
{
    private const string Prefix = "SmokeTest__";

    /// <summary>
    /// The version to test, like <c>1.2.0</c> or <c>1.2.0-rc.1</c>: what NuGet calls the packages. A release
    /// candidate's build metadata (<c>+57</c>) is not part of it.
    /// </summary>
    public string Version { get; } = Get("Version")?.TrimStart('v')
        ?? throw new InvalidOperationException($"Set the version to smoke test ({Prefix}Version).");

    /// <summary>The repository root: its CHANGELOG.md, the library's project file, a repository to snapshot.</summary>
    public string Root { get; } = Get("Root") ?? FindRoot();

    /// <summary>The feed our packages are tested from: a feed's URL, or a directory like a release candidate's.</summary>
    public string Source { get; } = Get("Source") ?? SmokeTestChecks.NuGetOrg;

    /// <summary>
    /// Where the previous releases (to upgrade from, restore old snapshots, compare the API and size with) and every
    /// package ours depend on come from.
    /// </summary>
    public string PreviousSource { get; } = Get("PreviousSource") ?? SmokeTestChecks.NuGetOrg;

    /// <summary>The user name for <see cref="Source"/>, when it needs one (GitHub Packages).</summary>
    public string? SourceUsername { get; } = Get("SourceUsername");

    /// <summary>The password or token for <see cref="Source"/>, when it needs one (GitHub Packages).</summary>
    public string? SourcePassword { get; } = Get("SourcePassword");

    /// <summary>
    /// How long to keep retrying while the feed doesn't list the packages, their metadata or symbols yet. nuget.org
    /// validates and indexes a package for several minutes after it is pushed.
    /// </summary>
    public TimeSpan Wait { get; } = TimeSpan.FromMinutes(Number("WaitMinutes", 30));

    /// <summary>
    /// A directory holding the GitHub release's assets. When set, they are checked against SHA256SUMS.txt, and the
    /// feed's packages, the symbol server's PDBs and the console app zip are compared with them.
    /// </summary>
    public string? ReleaseDirectory { get; } = Get("ReleaseDirectory");

    /// <summary>The commit the version was built from; otherwise the one the library package names.</summary>
    public string? Commit { get; } = Get("Commit");

    /// <summary>Whether to verify the packages' signatures. nuget.org signs every package; GitHub Packages doesn't.</summary>
    public bool VerifySignatures { get; } = Flag("VerifySignatures", true);

    /// <summary>Whether to check what the feed shows about the packages, with nuget.org's registration API.</summary>
    public bool FeedMetadata { get; } = Flag("FeedMetadata", true);

    /// <summary>Whether to download our assemblies' PDBs from <see cref="SymbolServer"/>.</summary>
    public bool Symbols { get; } = Flag("Symbols", true);

    /// <summary>The symbol server, in the SSQP format (<c>&lt;server&gt;/&lt;pdb&gt;/&lt;id&gt;/&lt;pdb&gt;</c>).</summary>
    public string SymbolServer { get; } = Get("SymbolServer") ?? "https://symbols.nuget.org/download/symbols";

    /// <summary>Whether to check the GitHub release. Reads it with <c>GITHUB_TOKEN</c>, when set.</summary>
    public bool GitHubRelease { get; } = Flag("GitHubRelease", true);

    /// <summary>The repository the GitHub release is in, as <c>owner/name</c>.</summary>
    public string GitHubRepository { get; } =
        Get("GitHubRepository") ?? Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") ?? "BusHero/GitSnapshotter";

    /// <summary>Whether to check <see cref="DocsPage"/> against the release.</summary>
    public bool Docs { get; } = Flag("Docs", true);

    /// <summary>The published installation page.</summary>
    public string DocsPage { get; } = Get("DocsPage") ?? "https://bushero.github.io/GitSnapshotter/installation/";

    /// <summary>How much (in percent) each framework's part of a package may grow over the previous release.</summary>
    public int MaxGrowthPercent { get; } = Number("MaxGrowthPercent", 25);

    /// <summary>
    /// Whether the version must sort above every stable release: on for release candidates, which are the next
    /// release. Off for releases, as a fix for an older line (a 0.3.1 after 0.4.0) may sort below.
    /// </summary>
    public bool AboveReleases { get; } = Flag("AboveReleases", false);

    private static string? Get(string name) =>
        Environment.GetEnvironmentVariable(Prefix + name) is { Length: > 0 } value ? value.Trim() : null;

    private static bool Flag(string name, bool fallback) =>
        Get(name) is { } value ? bool.Parse(value) : fallback;

    private static int Number(string name, int fallback) =>
        Get(name) is { } value ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;

    // Run with `dotnet run` from anywhere in the repository: the nearest directory with a .git in it.
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"Not in a git repository; set {Prefix}Root.");
    }
}
