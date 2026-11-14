using Microsoft.Extensions.Options;

using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Models;
using ModularPipelines.Modules;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>
/// Packs the library and the console app's .NET tool package as prereleases named after the branch and the build
/// time, each with its third-party notices. Both get the same version suffix.
/// </summary>
[DependsOn<CompileModule>]
[DependsOn<ThirdPartyNoticesModule>]
// Packing the tool publishes the console app internally; keep that apart from PublishConsoleModule's publish.
[DependsOn<PublishConsoleModule>]
public sealed class PackModule(IOptions<BuildSettings> settings) : Module<string>
{
    protected override async Task<string?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var suffix = PackageVersion.Suffix(context, settings.Value);
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
                    VersionSuffix = suffix,
                    Output = output,
                    Nologo = true,
                    Properties = [new KeyValue("ThirdPartyNoticesFile", thirdPartyNotices)],
                },
                cancellationToken: cancellationToken);
    }
}
