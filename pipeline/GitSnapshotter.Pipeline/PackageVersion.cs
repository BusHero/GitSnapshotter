using ModularPipelines.Context;
using ModularPipelines.Git.Extensions;

namespace GitSnapshotter.Pipeline;

/// <summary>The version both packages (the library and the .NET tool) get from one pipeline run.</summary>
internal static class PackageVersion
{
    // Taken once, so both packages of a prerelease build share the same timestamp.
    private static readonly DateTime BuildTime = DateTime.UtcNow;

    /// <summary>
    /// The prerelease suffix for <c>VersionPrefix</c>: none on a release build (the version comes from
    /// <see cref="BuildSettings.VersionProperties"/>), otherwise the branch and the build time.
    /// </summary>
    public static string? Suffix(IPipelineContext context, BuildSettings settings)
    {
        if (settings.IsRelease)
        {
            return null;
        }

        var branch = (context.Git().Information.BranchName ?? "local").Replace('/', '-');
        return $"{branch}-{BuildTime:yyyyMMddHHmmss}";
    }
}
