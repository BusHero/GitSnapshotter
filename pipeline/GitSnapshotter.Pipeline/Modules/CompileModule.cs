using Microsoft.Extensions.Options;

using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Models;
using ModularPipelines.Modules;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>
/// Restores and builds the solution, stamped with the build's version (<see cref="BuildVersionModule"/>): pack
/// doesn't rebuild, so the assemblies (and the tool's <c>--version</c>) must already carry the packages' version.
/// </summary>
// After the format check: both touch obj/, and running them together breaks the build.
// Optional: the Test and Compile targets leave the format check out.
[DependsOn<VerifyFormattingModule>(Optional = true)]
[DependsOn<BuildVersionModule>]
public sealed class CompileModule(IOptions<BuildSettings> settings) : Module<CommandResult>
{
    protected override async Task<CommandResult?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var version = (await context.GetModule<BuildVersionModule>()).ValueOrDefault!;

        return await context.DotNet().Build(
            new DotNetBuildOptions
            {
                ProjectSolution = Paths.Solution(context),
                Configuration = settings.Value.Configuration,
                Nologo = true,
                Properties = [VersionProperty(version)],
            },
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// The MSBuild property that stamps <paramref name="version"/> on what is built: <c>Version=0.4.1+57</c>. The SDK
    /// gives the assemblies <c>0.4.1.0</c> and the informational version <c>0.4.1+57.&lt;commit&gt;</c>; NuGet names
    /// the packages <c>0.4.1</c> and keeps <c>0.4.1+57</c> in their nuspec.
    /// </summary>
    internal static KeyValue VersionProperty(BuildVersion version) => new("Version", version.Full);
}
