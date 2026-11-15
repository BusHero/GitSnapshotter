using Microsoft.Extensions.Options;

using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Models;
using ModularPipelines.Modules;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>Publishes the console app, at the build's version (<see cref="BuildVersionModule"/>), to the publish directory.</summary>
[DependsOn<CompileModule>]
[DependsOn<BuildVersionModule>]
public sealed class PublishConsoleModule(IOptions<BuildSettings> settings) : Module<CommandResult>
{
    /// <summary>The target framework of the published console app.</summary>
    public const string ConsoleFramework = "net8.0";

    protected override async Task<CommandResult?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var version = (await context.GetModule<BuildVersionModule>()).ValueOrDefault!;

        return await context.DotNet().Publish(
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
                Properties = [CompileModule.VersionProperty(version)],
            },
            cancellationToken: cancellationToken);
    }
}
