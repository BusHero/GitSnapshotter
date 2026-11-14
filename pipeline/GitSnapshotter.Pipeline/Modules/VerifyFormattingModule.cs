using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Models;
using ModularPipelines.Modules;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>
/// Fails the build when the solution does not match the rules in .editorconfig.
/// Run <c>dotnet format Source/GitSnapshotter.slnx</c> to fix the reported files.
/// </summary>
public sealed class VerifyFormattingModule : Module<CommandResult>
{
    protected override async Task<CommandResult?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken) =>
        await context.DotNet().Format(
            new DotNetFormatOptions
            {
                ProjectSolution = Paths.Solution(context),
                VerifyNoChanges = true,
            },
            cancellationToken: cancellationToken);
}
