using Microsoft.Extensions.Options;

using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.GitHub.Extensions;
using ModularPipelines.Models;
using ModularPipelines.Modules;
using ModularPipelines.Options;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>
/// Runs the smoke tests (pipeline/GitSnapshotter.SmokeTests) on <see cref="BuildSettings.Version"/>: they download the
/// published packages and use them as a user would. Builds nothing of the product. Passes them the version, the
/// repository root and the GitHub repository; their other settings are <c>SmokeTest__*</c> environment variables,
/// which they read themselves.
/// </summary>
public sealed class SmokeTestModule(IOptions<BuildSettings> settings) : Module<CommandResult>
{
    protected override async Task<CommandResult?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var version = settings.Value.Version
            ?? throw new InvalidOperationException("Set the version to smoke test (Build__Version).");
        var github = context.GitHub().RepositoryInfo;

        return await context.DotNet().Run(
            new DotNetRunOptions
            {
                Project = Paths.SmokeTests(context),
                Configuration = settings.Value.Configuration,
                // Every test's output, so a run on a build server shows what it checked; and a limit, so a run that
                // hangs fails with what each test was doing instead of waiting for the job to be cancelled.
                Arguments = ["--", "--output", "Detailed", "--timeout", "45m"],
            },
            new CommandExecutionOptions
            {
                EnvironmentVariables = new Dictionary<string, string?>
                {
                    ["SmokeTest__Version"] = version,
                    ["SmokeTest__Root"] = Paths.Root(context),
                    ["SmokeTest__GitHubRepository"] = github.Owner is { } owner ? $"{owner}/{github.RepositoryName}" : null,
                },
            },
            cancellationToken);
    }
}
