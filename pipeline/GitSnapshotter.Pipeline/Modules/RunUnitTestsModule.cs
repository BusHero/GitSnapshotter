using Constants;

using Microsoft.Extensions.Options;

using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Models;
using ModularPipelines.Modules;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>
/// Runs the test suite. The exploratory LibGit2Sharp tests document library behaviour and are left out.
/// </summary>
[DependsOn<CompileModule>]
public sealed class RunUnitTestsModule(IOptions<BuildSettings> settings) : Module<CommandResult>
{
    private static readonly string Filter =
        $"{TestConstants.Traits.Names.CATEGORY}!={TestConstants.Traits.Values.DISCOVERY}";

    protected override async Task<CommandResult?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken) =>
        await context.DotNet().Test(
            new DotNetTestOptions
            {
                Configuration = settings.Value.Configuration,
                NoBuild = true,
                NoRestore = true,
                Arguments = [Paths.Solution(context), "--filter", Filter, "--nologo"],
            },
            cancellationToken: cancellationToken);
}
