using Microsoft.Extensions.Options;

using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Models;
using ModularPipelines.Modules;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>Restores and builds the solution.</summary>
public sealed class CompileModule(IOptions<BuildSettings> settings) : Module<CommandResult>
{
    protected override async Task<CommandResult?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken) =>
        await context.DotNet().Build(
            new DotNetBuildOptions
            {
                ProjectSolution = Paths.Solution(context),
                Configuration = settings.Value.Configuration,
                Nologo = true,
            },
            cancellationToken: cancellationToken);
}
