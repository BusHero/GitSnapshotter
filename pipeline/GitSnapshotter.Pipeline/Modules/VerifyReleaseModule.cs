using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModularPipelines.Attributes;
using ModularPipelines.Configuration;
using ModularPipelines.Context;
using ModularPipelines.Git.Extensions;
using ModularPipelines.GitHub.Extensions;
using ModularPipelines.Models;
using ModularPipelines.Modules;

using Octokit;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>
/// Checks that the release may go ahead, before anything is built: the run is on the default branch, the version is
/// not released yet, and the release environment still requires a reviewer (the workflow fails closed if anyone
/// removes that protection). Needs a GitHub token (<c>GITHUB_TOKEN</c>) that can read the repository's environments.
/// </summary>
[DependsOn<ReleaseVersionModule>]
public sealed class VerifyReleaseModule(IOptions<BuildSettings> settings) : Module<bool>
{
    protected override ModuleConfiguration Configure() =>
        ModuleConfiguration.Create()
            .WithSkipWhen(() => settings.Value.IsRelease
                ? SkipDecision.DoNotSkip
                : SkipDecision.Skip("Not a release"))
            .Build();

    protected override async Task<bool> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var release = (await context.GetModule<ReleaseVersionModule>()).ValueOrDefault!;
        var github = context.GitHub();
        var owner = github.RepositoryInfo.Owner
            ?? throw new InvalidOperationException("The repository has no GitHub remote to release from.");
        var repository = github.RepositoryInfo.RepositoryName!;
        var commit = Environment.GetEnvironmentVariable("GITHUB_SHA") is { Length: > 0 } sha
            ? sha
            : context.Git().Information.LastCommitSha!;

        await context.SubModule("Run is on the default branch", () => VerifyBranch(context));
        await context.SubModule(
            "Version is not released yet",
            () => VerifyNotReleased(context, github.Client, owner, repository, release.Tag, commit));
        await context.SubModule(
            "Release environment requires a reviewer",
            () => VerifyEnvironment(context, github.Client, owner, repository, cancellationToken));

        return true;
    }

    // A run started on a feature branch must not become a release.
    private Task VerifyBranch(IModuleContext context)
    {
        var git = context.Git().Information;
        var defaultBranch = settings.Value.DefaultBranch ?? git.DefaultBranchName;
        var reference = Environment.GetEnvironmentVariable("GITHUB_REF") is { Length: > 0 } githubRef
            ? githubRef
            : $"refs/heads/{git.BranchName}";

        return reference == $"refs/heads/{defaultBranch}"
            ? Task.CompletedTask
            : throw new InvalidOperationException($"Started on {reference}; run the release workflow on {defaultBranch}");
    }

    // Each version is released once. A re-run finds its own tag on the same commit and goes on.
    private static async Task VerifyNotReleased(
        IModuleContext context, IGitHubClient client, string owner, string repository, string tag, string commit)
    {
        string? tagged;
        try
        {
            tagged = (await client.Repository.Commit.Get(owner, repository, $"refs/tags/{tag}")).Sha;
        }
        catch (Exception exception) when (exception is NotFoundException or ApiValidationException)
        {
            // 404, or 422 when the reference names no commit: the tag does not exist.
            tagged = null;
        }

        if (string.IsNullOrEmpty(tagged))
        {
            context.Logger.LogInformation("{Tag} does not exist yet", tag);
            return;
        }

        if (!string.Equals(tagged, commit, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{tag} already exists on {tagged}; pick a new version");
        }

        context.Logger.LogInformation("{Tag} already points at {Commit}: continuing an earlier attempt", tag, commit);
    }

    // The publish job's approval gate lives in the repository settings, so check here that it is still there.
    // Octokit has no model for an environment's protection rules, so this reads the REST API directly.
    private async Task VerifyEnvironment(
        IModuleContext context, IGitHubClient client, string owner, string repository, CancellationToken cancellationToken)
    {
        var environment = settings.Value.ReleaseEnvironment;
        var unreadable = $"Could not read the '{environment}' environment. Create it in Settings > Environments and "
            + "add a required reviewer.";

        using var http = new HttpClient { BaseAddress = client.Connection.BaseAddress };
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"repos/{owner}/{repository}/environments/{Uri.EscapeDataString(environment)}", UriKind.Relative));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("GitSnapshotter.Pipeline");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", client.Connection.Credentials.Password);

        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"{unreadable} ({(int)response.StatusCode} {response.ReasonPhrase})");
        }

        var reviewers = CountRequiredReviewers(await response.Content.ReadAsByteArrayAsync(cancellationToken));
        if (reviewers < 1)
        {
            throw new InvalidOperationException(
                $"The '{environment}' environment has no required reviewers. Add one in Settings > Environments so "
                + "every release is approved by hand.");
        }

        context.Logger.LogInformation(
            "'{Environment}' requires approval from {Reviewers} reviewer(s)", environment, reviewers);
    }

    private static int CountRequiredReviewers(byte[] body)
    {
        using var json = JsonDocument.Parse(body);
        if (!json.RootElement.TryGetProperty("protection_rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return rules.EnumerateArray()
            .Where(rule => rule.TryGetProperty("type", out var type) && type.GetString() == "required_reviewers")
            .Sum(rule => rule.TryGetProperty("reviewers", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.GetArrayLength()
                : 0);
    }
}
