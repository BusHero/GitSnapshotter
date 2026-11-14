using Microsoft.Extensions.Options;

using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Git.Extensions;
using ModularPipelines.Modules;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>
/// Publishes the console app and returns the build version: the release version on a release build, otherwise
/// <c>yyyyMMddHHmmss+trunk</c> (or <c>+merge</c> off the default branch). On GitHub Actions the version is also
/// written to the step's <c>version</c> output.
/// </summary>
[DependsOn<CompileModule>]
public sealed class PublishConsoleModule(IOptions<BuildSettings> settings) : Module<string>
{
    protected override async Task<string?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        await context.DotNet().Publish(
            new DotNetPublishOptions
            {
                ProjectSolution = Paths.Console(context),
                Configuration = settings.Value.Configuration,
                NoBuild = true,
                NoRestore = true,
                Output = Paths.PublishDirectory(context, settings.Value),
                Nologo = true,
                Properties = settings.Value.VersionProperties,
            },
            cancellationToken: cancellationToken);

        var version = settings.Value.IsRelease ? settings.Value.Version! : BuildVersion(context);

        await GitHubActions.SetOutputAsync("version", version, cancellationToken);

        return version;
    }

    private string BuildVersion(IModuleContext context)
    {
        var git = context.Git().Information;
        var defaultBranch = settings.Value.DefaultBranch ?? git.DefaultBranchName;
        return $"{DateTime.UtcNow:yyyyMMddHHmmss}+{(git.BranchName == defaultBranch ? "trunk" : "merge")}";
    }
}
