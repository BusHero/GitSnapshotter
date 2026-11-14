using ModularPipelines.Configuration;
using ModularPipelines.Context;
using ModularPipelines.Git.Extensions;
using ModularPipelines.Git.Options;
using ModularPipelines.Models;
using ModularPipelines.Modules;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>
/// Gives git a user name and email on a build server, which has none: the tests create commits. Skipped on a
/// developer machine, so it never changes a developer's git configuration.
/// </summary>
public sealed class ConfigureGitIdentityModule : Module<bool>
{
    private const string Name = "github-actions";

    private const string Email = "github-actions@github.com";

    protected override ModuleConfiguration Configure() =>
        ModuleConfiguration.Create()
            .WithSkipWhen(context => context.Environment.BuildSystem.IsBuildServer
                ? SkipDecision.DoNotSkip
                : SkipDecision.Skip("Not on a build server; the tests use your own git identity"))
            .Build();

    protected override async Task<bool> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        await Set("user.email", Email);
        await Set("user.name", Name);
        return true;

        Task Set(string key, string value) =>
            context.Git().Commands.Config(
                new GitConfigOptions { Global = true, Arguments = [key, value] },
                null,
                cancellationToken);
    }
}
