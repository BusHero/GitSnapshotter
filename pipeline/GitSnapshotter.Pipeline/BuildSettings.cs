using ModularPipelines.Attributes;

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
}
