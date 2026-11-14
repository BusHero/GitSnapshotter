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

    /// <summary>The build configuration.</summary>
    public string Configuration { get; set; } = "Release";

    /// <summary>Where the NuGet package and the published console app go, relative to the repository root.</summary>
    public string PublishDirectory { get; set; } = "publish";

    /// <summary>The repository's default branch. Detected from git when not set.</summary>
    public string? DefaultBranch { get; set; }

    /// <summary>The feed the package is pushed to.</summary>
    public string NuGetSource { get; set; } = "https://nuget.pkg.github.com/BusHero/index.json";

    /// <summary>The API key for <see cref="NuGetSource"/>. When empty, the package is not pushed.</summary>
    [SecretValue]
    public string? NuGetApiKey { get; set; }

    /// <summary>
    /// The release version, like <c>1.2.0</c> or <c>1.2.0-rc.1</c>. When set, the assemblies, the package and the
    /// console app get exactly this version; otherwise the package is a prerelease named after the branch.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>Whether this is a release build (<see cref="Version"/> is set).</summary>
    internal bool IsRelease => !string.IsNullOrEmpty(Version);

    /// <summary>The MSBuild properties that stamp <see cref="Version"/> on a release build; none otherwise.</summary>
    internal KeyValue[] VersionProperties => IsRelease ? [new KeyValue("Version", Version!)] : [];
}
