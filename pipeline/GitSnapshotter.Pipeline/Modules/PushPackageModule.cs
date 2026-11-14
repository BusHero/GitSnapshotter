using Microsoft.Extensions.Options;

using ModularPipelines.Attributes;
using ModularPipelines.Configuration;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Models;
using ModularPipelines.Modules;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>Pushes the packages (library and tool) once the tests have passed. Skipped when no API key is configured.</summary>
[DependsOn<PackModule>]
[DependsOn<RunUnitTestsModule>]
public sealed class PushPackageModule(IOptions<BuildSettings> settings) : Module<CommandResult>
{
    protected override ModuleConfiguration Configure() =>
        ModuleConfiguration.Create()
            .WithSkipWhen(() => string.IsNullOrEmpty(settings.Value.NuGetApiKey)
                ? SkipDecision.Skip("No NuGet API key (set Build__NuGetApiKey)")
                : SkipDecision.DoNotSkip)
            .Build();

    protected override async Task<CommandResult?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var packageDirectory = await context.GetModule<PackModule>();

        return await context.DotNet().Nuget.Push(
            new DotNetNugetPushOptions
            {
                Path = Path.Combine(packageDirectory.ValueOrDefault!, "*.nupkg"),
                Source = settings.Value.NuGetSource,
                ApiKey = settings.Value.NuGetApiKey,
                SkipDuplicate = true,
            },
            cancellationToken: cancellationToken);
    }
}
