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

    /// <summary>Whether <see cref="VerifyNotReleased"/> found the release's tag.</summary>
    internal enum TagState
    {
        /// <summary>The tag doesn't exist: this is the first attempt at the release.</summary>
        Missing,

        /// <summary>The tag is on the commit being released: an earlier attempt created it.</summary>
        OnThisCommit,
    }

    private Task VerifyBranch(IModuleContext context)
    {
        var git = context.Git().Information;
        VerifyBranch(
            Environment.GetEnvironmentVariable("GITHUB_REF"),
            git.BranchName,
            settings.Value.DefaultBranch ?? git.DefaultBranchName);
        return Task.CompletedTask;
    }

    /// <summary>A run started on a feature branch must not become a release.</summary>
    /// <param name="githubRef">The ref GitHub Actions started the run on (<c>GITHUB_REF</c>), if any.</param>
    /// <param name="branch">The branch checked out, used when there is no <paramref name="githubRef"/>.</param>
    /// <param name="defaultBranch">The repository's default branch.</param>
    /// <exception cref="InvalidOperationException">The run is not on the default branch.</exception>
    internal static void VerifyBranch(string? githubRef, string? branch, string? defaultBranch)
    {
        var reference = string.IsNullOrEmpty(githubRef) ? $"refs/heads/{branch}" : githubRef;

        if (string.IsNullOrEmpty(defaultBranch) || reference != $"refs/heads/{defaultBranch}")
        {
            throw new InvalidOperationException($"Started on {reference}; run the release workflow on {defaultBranch}");
        }
    }

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

        if (CheckTag(tag, tagged, commit) == TagState.Missing)
        {
            context.Logger.LogInformation("{Tag} does not exist yet", tag);
        }
        else
        {
            context.Logger.LogInformation("{Tag} already points at {Commit}: continuing an earlier attempt", tag, commit);
        }
    }

    /// <summary>Each version is released once. A re-run finds its own tag on the same commit and goes on.</summary>
    /// <param name="tag">The release's tag.</param>
    /// <param name="tagged">The commit the tag points at; none when there is no such tag.</param>
    /// <param name="commit">The commit being released.</param>
    /// <exception cref="InvalidOperationException">The tag is on another commit: the version is released already.</exception>
    internal static TagState CheckTag(string tag, string? tagged, string commit)
    {
        if (string.IsNullOrEmpty(tagged))
        {
            return TagState.Missing;
        }

        return string.Equals(tagged, commit, StringComparison.OrdinalIgnoreCase)
            ? TagState.OnThisCommit
            : throw new InvalidOperationException($"{tag} already exists on {tagged}; pick a new version");
    }

    // The publish job's approval gate lives in the repository settings, so check here that it is still there.
    private async Task VerifyEnvironment(
        IModuleContext context, IGitHubClient client, string owner, string repository, CancellationToken cancellationToken)
    {
        var environment = settings.Value.ReleaseEnvironment;

        using var http = new HttpClient { BaseAddress = client.Connection.BaseAddress };
        using var request = EnvironmentRequest(owner, repository, environment, client.Connection.Credentials.Password);
        using var response = await http.SendAsync(request, cancellationToken);
        var reviewers = await RequiredReviewersAsync(response, environment, cancellationToken);

        context.Logger.LogInformation(
            "'{Environment}' requires approval from {Reviewers} reviewer(s)", environment, reviewers);
    }

    /// <summary>
    /// The REST API request for an environment, relative to the API's base address. Octokit has no model for an
    /// environment's protection rules, so the module reads the API directly.
    /// </summary>
    internal static HttpRequestMessage EnvironmentRequest(string owner, string repository, string environment, string token)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"repos/{owner}/{repository}/environments/{Uri.EscapeDataString(environment)}", UriKind.Relative));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("GitSnapshotter.Pipeline");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    /// <summary>The number of required reviewers in a response to <see cref="EnvironmentRequest"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// The environment couldn't be read, or it has no required reviewers. The release fails closed either way.
    /// </exception>
    internal static async Task<int> RequiredReviewersAsync(
        HttpResponseMessage response, string environment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"Could not read the '{environment}' environment. Create it in Settings > Environments and add a "
                + $"required reviewer. ({(int)response.StatusCode} {response.ReasonPhrase})");
        }

        var reviewers = CountRequiredReviewers(await response.Content.ReadAsByteArrayAsync(cancellationToken));
        return reviewers >= 1
            ? reviewers
            : throw new InvalidOperationException(
                $"The '{environment}' environment has no required reviewers. Add one in Settings > Environments so "
                + "every release is approved by hand.");
    }

    /// <summary>
    /// The number of reviewers an environment's <c>required_reviewers</c> rules name, from the REST API's environment
    /// response; 0 when it has no such rule.
    /// </summary>
    internal static int CountRequiredReviewers(byte[] body)
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
