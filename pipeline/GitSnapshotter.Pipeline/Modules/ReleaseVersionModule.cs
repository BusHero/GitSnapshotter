using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModularPipelines.Configuration;
using ModularPipelines.Context;
using ModularPipelines.Models;
using ModularPipelines.Modules;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>
/// Checks that <see cref="BuildSettings.Version"/> is a SemVer version and returns it with its tag. On GitHub Actions
/// it also sets the step's <c>version</c>, <c>tag</c> and <c>prerelease</c> outputs. Skipped when no version is given.
/// </summary>
public sealed class ReleaseVersionModule(IOptions<BuildSettings> settings) : Module<ReleaseVersion>
{
    protected override ModuleConfiguration Configure() =>
        ModuleConfiguration.Create()
            .WithSkipWhen(() => settings.Value.IsRelease
                ? SkipDecision.DoNotSkip
                : SkipDecision.Skip("No version (set Build__Version), so this is not a release"))
            .Build();

    protected override async Task<ReleaseVersion?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var release = ReleaseVersion.Parse(settings.Value.Version!);

        await GitHubActions.SetOutputAsync("version", release.Version, cancellationToken);
        await GitHubActions.SetOutputAsync("tag", release.Tag, cancellationToken);
        await GitHubActions.SetOutputAsync("prerelease", release.IsPrerelease ? "true" : "false", cancellationToken);

        context.Logger.LogInformation(
            "Releasing {Version} as {Tag} ({Kind})",
            release.Version,
            release.Tag,
            release.IsPrerelease ? "prerelease" : "stable");

        return release;
    }
}
