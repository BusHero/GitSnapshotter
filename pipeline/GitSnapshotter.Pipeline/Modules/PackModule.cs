using Microsoft.Extensions.Options;

using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Models;
using ModularPipelines.Modules;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>
/// Packs the library and the console app's .NET tool package, each with its third-party notices, at the build's
/// version (<see cref="BuildVersionModule"/>). The only module that packs, so no two packs write to the publish
/// directory at once.
/// </summary>
[DependsOn<CompileModule>]
[DependsOn<BuildVersionModule>]
[DependsOn<ThirdPartyNoticesModule>]
// Packing the tool publishes the console app internally; keep that apart from PublishConsoleModule's publish.
[DependsOn<PublishConsoleModule>]
public sealed class PackModule(IOptions<BuildSettings> settings) : Module<string>
{
    protected override async Task<string?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var version = (await context.GetModule<BuildVersionModule>()).ValueOrDefault!;
        var output = Paths.PublishDirectory(context, settings.Value);
        var notices = await context.GetModule<ThirdPartyNoticesModule>();

        // The library's notices cover the packages compiled into it; the tool's cover every package it ships.
        await Pack(Paths.Library(context), notices.ValueOrDefault!.Library);
        await Pack(Paths.Console(context), notices.ValueOrDefault!.Console);

        return output;

        Task Pack(string project, string thirdPartyNotices) =>
            context.DotNet().Pack(
                new DotNetPackOptions
                {
                    ProjectSolution = project,
                    Configuration = settings.Value.Configuration,
                    NoBuild = true,
                    NoRestore = true,
                    Output = output,
                    Nologo = true,
                    Properties =
                    [
                        new KeyValue("ThirdPartyNoticesFile", thirdPartyNotices),
                        CompileModule.VersionProperty(version),
                    ],
                },
                cancellationToken: cancellationToken);
    }
}
