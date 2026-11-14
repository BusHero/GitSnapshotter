using Microsoft.Extensions.Options;

using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Models;
using ModularPipelines.Modules;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>
/// Restores and builds the solution, stamped with the release version on a release build, and otherwise with the
/// prerelease suffix <see cref="PackModule"/> packs with: pack doesn't rebuild, so the assemblies (and the tool's
/// <c>--version</c>) must already carry the packages' version.
/// </summary>
// After the format check: both touch obj/, and running them together breaks the build.
// Optional: the Test and Compile targets leave the format check out.
[DependsOn<VerifyFormattingModule>(Optional = true)]
public sealed class CompileModule(IOptions<BuildSettings> settings) : Module<CommandResult>
{
    protected override async Task<CommandResult?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken) =>
        await context.DotNet().Build(
            new DotNetBuildOptions
            {
                ProjectSolution = Paths.Solution(context),
                Configuration = settings.Value.Configuration,
                Nologo = true,
                Properties = settings.Value.VersionProperties,
                VersionSuffix = PackageVersion.Suffix(context, settings.Value),
            },
            cancellationToken: cancellationToken);
}
