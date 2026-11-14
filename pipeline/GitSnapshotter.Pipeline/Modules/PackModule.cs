using Microsoft.Extensions.Options;

using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Git.Extensions;
using ModularPipelines.Models;
using ModularPipelines.Modules;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>
/// Packs the library as a prerelease named after the branch and the build time, with the third-party notices.
/// </summary>
[DependsOn<CompileModule>]
[DependsOn<ThirdPartyNoticesModule>]
public sealed class PackModule(IOptions<BuildSettings> settings) : Module<string>
{
    protected override async Task<string?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var branch = (context.Git().Information.BranchName ?? "local").Replace('/', '-');
        var suffix = $"{branch}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var output = Paths.PublishDirectory(context, settings.Value);
        var notices = await context.GetModule<ThirdPartyNoticesModule>();

        await context.DotNet().Pack(
            new DotNetPackOptions
            {
                ProjectSolution = Paths.Library(context),
                Configuration = settings.Value.Configuration,
                NoBuild = true,
                NoRestore = true,
                VersionSuffix = suffix,
                Output = output,
                Nologo = true,
                Properties = [new KeyValue("ThirdPartyNoticesFile", notices.ValueOrDefault!.Library)],
            },
            cancellationToken: cancellationToken);

        return output;
    }
}
