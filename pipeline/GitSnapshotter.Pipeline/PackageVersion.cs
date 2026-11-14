using System.Globalization;

using ModularPipelines.Context;
using ModularPipelines.Git.Extensions;

namespace GitSnapshotter.Pipeline;

/// <summary>The versions one pipeline run gives the packages (the library and the .NET tool) and the console app.</summary>
internal static class PackageVersion
{
    // Taken once, so both packages of a prerelease build share the same timestamp.
    private static readonly DateTime BuildTime = DateTime.UtcNow;

    /// <summary>
    /// The prerelease suffix for <c>VersionPrefix</c>: none on a release build (the version comes from
    /// <see cref="BuildSettings.VersionProperties"/>), otherwise the branch and the build time.
    /// </summary>
    public static string? Suffix(IPipelineContext context, BuildSettings settings) =>
        Suffix(settings, context.Git().Information.BranchName, BuildTime);

    /// <inheritdoc cref="Suffix(IPipelineContext, BuildSettings)"/>
    /// <param name="settings">The pipeline settings.</param>
    /// <param name="branch">The branch being built; none on a detached HEAD.</param>
    /// <param name="buildTime">When the build started, in UTC.</param>
    public static string? Suffix(BuildSettings settings, string? branch, DateTime buildTime)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.IsRelease)
        {
            return null;
        }

        // A suffix is a single prerelease label part: "feature/x" would not be a valid version.
        branch = string.IsNullOrEmpty(branch) ? "local" : branch.Replace('/', '-');
        return string.Create(CultureInfo.InvariantCulture, $"{branch}-{buildTime:yyyyMMddHHmmss}");
    }

    /// <summary>
    /// The console app's version: the release version on a release build, otherwise <c>yyyyMMddHHmmss+trunk</c>, or
    /// <c>+merge</c> off the default branch.
    /// </summary>
    /// <param name="settings">The pipeline settings.</param>
    /// <param name="branch">The branch being built; none on a detached HEAD.</param>
    /// <param name="defaultBranch">The repository's default branch, when <see cref="BuildSettings.DefaultBranch"/> isn't set.</param>
    /// <param name="buildTime">When the build started, in UTC.</param>
    public static string Console(BuildSettings settings, string? branch, string? defaultBranch, DateTime buildTime)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.IsRelease)
        {
            return settings.Version!;
        }

        var onDefaultBranch = branch == (settings.DefaultBranch ?? defaultBranch);
        return string.Create(
            CultureInfo.InvariantCulture, $"{buildTime:yyyyMMddHHmmss}+{(onDefaultBranch ? "trunk" : "merge")}");
    }

    /// <inheritdoc cref="Console(BuildSettings, string, string, DateTime)"/>
    public static string Console(IPipelineContext context, BuildSettings settings)
    {
        var git = context.Git().Information;
        return Console(settings, git.BranchName, git.DefaultBranchName, DateTime.UtcNow);
    }

    /// <summary>
    /// The version the library was packed at, from the package file names in the publish directory: the one ending
    /// in <paramref name="suffix"/> on a CI build, the release version otherwise.
    /// </summary>
    public static string? Packed(IEnumerable<string> fileNames, string? suffix) =>
        fileNames
            .Where(name => name.StartsWith("GitSnapshotter.", StringComparison.Ordinal)
                && !name.StartsWith("GitSnapshotter.Tool.", StringComparison.Ordinal)
                && name.EndsWith(".nupkg", StringComparison.Ordinal))
            .Select(name => name["GitSnapshotter.".Length..^".nupkg".Length])
            .Where(version => suffix is null || version.EndsWith("-" + suffix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .LastOrDefault();
}
