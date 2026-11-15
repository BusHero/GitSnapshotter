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
    /// Where the build puts the release candidate, the files a release publishes, relative to the repository root.
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

    /// <summary>
    /// Which build this is, the build metadata of its version (<c>0.4.1+57</c>): the CI run number. Builds without one
    /// are <c>+local</c>.
    /// </summary>
    public string? BuildNumber { get; set; }

    /// <summary>
    /// The version to release (<see cref="BuildTarget.VerifyRelease"/>) or smoke test (<see cref="BuildTarget.SmokeTest"/>),
    /// like <c>1.2.0</c> or <c>1.2.0-rc.1</c>: the version NuGet knows the packages by, without build metadata. A
    /// leading <c>v</c>, as in a tag name, is dropped. Builds don't use it: they work out their own version
    /// (<see cref="Modules.BuildVersionModule"/>).
    /// </summary>
    public string? Version
    {
        get;
        set => field = string.IsNullOrWhiteSpace(value) ? null : ReleaseVersion.Normalize(value.Trim());
    }

    /// <summary>
    /// The commit being released, for <see cref="BuildTarget.VerifyRelease"/>: the release candidate's, which is
    /// older than the commit the release workflow runs on. Defaults to <c>GITHUB_SHA</c>, then to HEAD.
    /// </summary>
    public string? Commit { get; set; }

    /// <summary>Whether a version to release or smoke test is given (<see cref="Version"/> is set).</summary>
    internal bool IsRelease => !string.IsNullOrEmpty(Version);
}
