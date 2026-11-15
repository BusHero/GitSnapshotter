using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModularPipelines.Context;
using ModularPipelines.Git.Extensions;
using ModularPipelines.Git.Options;
using ModularPipelines.Modules;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>
/// Works out the build's version (<see cref="PackageVersion"/>): the next release after the tagged ones, from the
/// <c>VersionPrefix</c> in Source/Directory.Build.props, with <see cref="BuildSettings.BuildNumber"/> as build
/// metadata, like <c>0.4.1+57</c>. On GitHub Actions it sets the step's <c>version</c> (<c>0.4.1</c>, what NuGet calls
/// the packages) and <c>build-version</c> (<c>0.4.1+57</c>) outputs.
/// </summary>
public sealed class BuildVersionModule(IOptions<BuildSettings> settings) : Module<BuildVersion>
{
    protected override async Task<BuildVersion?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var prefix = PackageVersion.ReadPrefix(Paths.VersionProps(context));
        var tags = await TagsAsync(context, cancellationToken);
        if (tags.Count == 0)
        {
            context.Logger.LogWarning(
                "No release tags in this clone, so the version is <VersionPrefix> {Prefix} even if that is released. "
                + "Run 'git fetch --tags' (on CI, check out with fetch-depth: 0).",
                prefix.Version);
        }

        var version = new BuildVersion(PackageVersion.Next(prefix, tags), PackageVersion.Build(settings.Value.BuildNumber));

        await GitHubActions.SetOutputAsync("version", version.Version, cancellationToken);
        await GitHubActions.SetOutputAsync("build-version", version.Full, cancellationToken);

        context.Logger.LogInformation(
            "Building {Full}: <VersionPrefix> {Prefix}, {Count} release tag(s)", version.Full, prefix.Version, tags.Count);

        return version;
    }

    private static async Task<IReadOnlyList<string>> TagsAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var result = await context.Git().Commands.Tag(
            new GitTagOptions { Arguments = ["--list", "v*"] },
            null,
            cancellationToken);

        return result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
