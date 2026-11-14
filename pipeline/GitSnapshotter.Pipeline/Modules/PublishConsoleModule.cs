using Microsoft.Extensions.Options;

using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
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
    /// <summary>The target framework of the published console app.</summary>
    public const string ConsoleFramework = "net8.0";

    protected override async Task<string?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        await context.DotNet().Publish(
            new DotNetPublishOptions
            {
                ProjectSolution = Paths.Console(context),
                // One build in the zip: the .NET 8 one, which runs on .NET 8 and every later runtime (RollForward).
                Framework = ConsoleFramework,
                Configuration = settings.Value.Configuration,
                NoBuild = true,
                NoRestore = true,
                Output = Paths.PublishDirectory(context, settings.Value),
                Nologo = true,
                Properties = settings.Value.VersionProperties,
            },
            cancellationToken: cancellationToken);

        var version = PackageVersion.Console(context, settings.Value);

        await GitHubActions.SetOutputAsync("version", version, cancellationToken);

        return version;
    }
}
