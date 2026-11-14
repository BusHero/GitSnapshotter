using Microsoft.Extensions.Options;

using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Git.Extensions;
using ModularPipelines.Modules;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>
/// Publishes the console app and returns the build version (<c>yyyyMMddHHmmss+trunk</c>, or <c>+merge</c> off the
/// default branch). On GitHub Actions the version is also written to the step's <c>version</c> output.
/// </summary>
[DependsOn<CompileModule>]
public sealed class PublishConsoleModule(IOptions<BuildSettings> settings) : Module<string>
{
    protected override async Task<string?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        await context.DotNet().Publish(
            new DotNetPublishOptions
            {
                ProjectSolution = Paths.Console(context),
                Configuration = settings.Value.Configuration,
                NoBuild = true,
                NoRestore = true,
                Output = Paths.PublishDirectory(context, settings.Value),
                Nologo = true,
            },
            cancellationToken: cancellationToken);

        var git = context.Git().Information;
        var defaultBranch = settings.Value.DefaultBranch ?? git.DefaultBranchName;
        var version = $"{DateTime.UtcNow:yyyyMMddHHmmss}+{(git.BranchName == defaultBranch ? "trunk" : "merge")}";

        var githubOutput = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
        if (!string.IsNullOrEmpty(githubOutput))
        {
            await File.AppendAllTextAsync(githubOutput, $"version={version}\n", cancellationToken);
        }

        return version;
    }
}
