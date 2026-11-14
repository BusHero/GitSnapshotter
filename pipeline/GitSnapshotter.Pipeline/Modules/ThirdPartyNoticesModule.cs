using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.Modules;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>Where <see cref="ThirdPartyNoticesModule"/> wrote the notices.</summary>
/// <param name="Console">The console app's notices, in the publish directory next to the app.</param>
/// <param name="Library">The NuGet package's notices, which <see cref="PackModule"/> puts in the package.</param>
public sealed record ThirdPartyNoticesFiles(string Console, string Library);

/// <summary>
/// Generates THIRD-PARTY-NOTICES.txt for the console app (every package it ships) and for the NuGet package (the
/// packages compiled into the library). Depends on <see cref="CompileModule"/> for the restore graph.
/// </summary>
[DependsOn<CompileModule>]
public sealed class ThirdPartyNoticesModule(IOptions<BuildSettings> settings) : Module<ThirdPartyNoticesFiles>
{
    protected override async Task<ThirdPartyNoticesFiles?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var console = Path.Combine(Paths.PublishDirectory(context, settings.Value), ThirdPartyNotices.FileName);
        var library = Path.Combine(Paths.IntermediateDirectory(Paths.Library(context)), ThirdPartyNotices.FileName);

        var consolePackages = await ThirdPartyNotices.WriteAsync(
            "GitSnapshotter.Console",
            Paths.AssetsFile(Paths.Console(context)),
            ThirdPartyNotices.AppAssets,
            console,
            cancellationToken);

        var libraryPackages = await ThirdPartyNotices.WriteAsync(
            "The GitSnapshotter package",
            Paths.AssetsFile(Paths.Library(context)),
            ThirdPartyNotices.PackageAssets,
            library,
            cancellationToken);

        context.Logger.LogInformation("Console app notices: {Packages}", string.Join(", ", consolePackages));
        context.Logger.LogInformation("Package notices: {Packages}", string.Join(", ", libraryPackages));

        return new ThirdPartyNoticesFiles(console, library);
    }
}
