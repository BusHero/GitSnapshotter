using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Models;
using ModularPipelines.Modules;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>
/// Restores the solution, which writes the restore graph (obj/project.assets.json, transitive packages included) that
/// the dependency graph submission reads. The pipeline itself is restored by the <c>dotnet run</c> that starts it.
/// </summary>
public sealed class RestoreModule : Module<CommandResult>
{
    protected override async Task<CommandResult?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken) =>
        await context.DotNet().Restore(
            new DotNetRestoreOptions { ProjectSolution = Paths.Solution(context) },
            cancellationToken: cancellationToken);
}
