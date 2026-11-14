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
[DependsOn<ConfigureGitIdentityModule>]
public sealed class RunUnitTestsModule(IOptions<BuildSettings> settings) : Module<CommandResult>
{
    // TUnit tree filter: /assembly/namespace/class/test[property].
    private const string Filter =
        $"/*/*/*/*[{TestConstants.Traits.Names.CATEGORY}!={TestConstants.Traits.Values.DISCOVERY}]";

    protected override async Task<CommandResult?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken) =>
        await context.DotNet().Test(
            new DotNetTestOptions
            {
                Solution = Paths.Solution(context),
                Configuration = settings.Value.Configuration,
                NoBuild = true,
                NoRestore = true,
                Arguments = ["--treenode-filter", Filter],
            },
            cancellationToken: cancellationToken);
}
