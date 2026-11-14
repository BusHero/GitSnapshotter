using Microsoft.Extensions.Options;

using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Models;
using ModularPipelines.Modules;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>
/// Packs the console app as the <c>GitSnapshotter.Tool</c> .NET tool, with the same version as the library package
/// and the console app's third-party notices (the tool ships every package the app does).
/// </summary>
// After the console publish: packing a tool publishes the project again, and both write to its obj directory.
[DependsOn<PublishConsoleModule>]
[DependsOn<ThirdPartyNoticesModule>]
public sealed class PackToolModule(IOptions<BuildSettings> settings) : Module<string>
{
    protected override async Task<string?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var output = Paths.PublishDirectory(context, settings.Value);
        var notices = await context.GetModule<ThirdPartyNoticesModule>();

        await context.DotNet().Pack(
            new DotNetPackOptions
            {
                ProjectSolution = Paths.Console(context),
                Configuration = settings.Value.Configuration,
                NoBuild = true,
                NoRestore = true,
                VersionSuffix = PackageVersion.Suffix(context, settings.Value),
                Output = output,
                Nologo = true,
                Properties =
                [
                    new KeyValue("ThirdPartyNoticesFile", notices.ValueOrDefault!.Console),
                    .. settings.Value.VersionProperties,
                ],
            },
            cancellationToken: cancellationToken);

        return output;
    }
}
