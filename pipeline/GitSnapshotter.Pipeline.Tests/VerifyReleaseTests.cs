using System.Net;
using System.Text;

using GitSnapshotter.Pipeline.Modules;

using TUnit.Core;

namespace GitSnapshotter.Pipeline.Tests;

public sealed class VerifyReleaseTests
{
    private static int Count(string json) => VerifyReleaseModule.CountRequiredReviewers(Encoding.UTF8.GetBytes(json));

    [Test]
    public async Task ReviewersOfRequiredReviewerRulesAreCounted()
    {
        // Trimmed from GET /repos/{owner}/{repo}/environments/{name}.
        const string json = """
            {
              "name": "nuget",
              "protection_rules": [
                { "id": 1, "type": "wait_timer", "wait_timer": 30 },
                {
                  "id": 2,
                  "type": "required_reviewers",
                  "prevent_self_review": false,
                  "reviewers": [
                    { "type": "User", "reviewer": { "login": "someone" } },
                    { "type": "Team", "reviewer": { "slug": "maintainers" } }
                  ]
                },
                { "id": 3, "type": "branch_policy" }
              ]
            }
            """;

        await Assert.That(Count(json)).IsEqualTo(2);
    }

    [Test]
    [Arguments("""{ "name": "nuget" }""")]
    [Arguments("""{ "protection_rules": null }""")]
    [Arguments("""{ "protection_rules": {} }""")]
    [Arguments("""{ "protection_rules": [] }""")]
    [Arguments("""{ "protection_rules": [ { "type": "wait_timer", "wait_timer": 30 } ] }""")]
    [Arguments("""{ "protection_rules": [ { "type": "required_reviewers", "reviewers": [] } ] }""")]
    [Arguments("""{ "protection_rules": [ { "type": "required_reviewers" } ] }""")]
    [Arguments("""{ "protection_rules": [ { "type": "required_reviewers", "reviewers": null } ] }""")]
    [Arguments("""{ "protection_rules": [ { "reviewers": [ {} ] } ] }""")]
    public async Task EnvironmentWithoutRequiredReviewersCountsZero(string json)
    {
        // Every shape that means "no one has to approve" must fail the release check.
        await Assert.That(Count(json)).IsEqualTo(0);
    }

    [Test]
    [Arguments("refs/heads/trunk", "feature", "trunk")]
    [Arguments(null, "trunk", "trunk")]
    [Arguments("", "trunk", "trunk")]
    public async Task RunOnTheDefaultBranchMayRelease(string? githubRef, string branch, string defaultBranch)
    {
        await Assert.That(() => VerifyReleaseModule.VerifyBranch(githubRef, branch, defaultBranch)).ThrowsNothing();
    }

    [Test]
    [Arguments("refs/heads/feature", "trunk", "trunk", "refs/heads/feature")]
    [Arguments("refs/tags/v1.2.0", "trunk", "trunk", "refs/tags/v1.2.0")]
    [Arguments("refs/pull/7/merge", "trunk", "trunk", "refs/pull/7/merge")]
    [Arguments("refs/heads/trunk-old", "trunk", "trunk", "refs/heads/trunk-old")]
    [Arguments(null, "feature", "trunk", "refs/heads/feature")]
    [Arguments(null, null, "trunk", "refs/heads/")]
    [Arguments(null, null, null, "refs/heads/")]
    [Arguments("refs/heads/", "trunk", "", "refs/heads/")]
    public async Task RunOffTheDefaultBranchMayNotRelease(
        string? githubRef, string? branch, string? defaultBranch, string reported)
    {
        await Assert.That(() => VerifyReleaseModule.VerifyBranch(githubRef, branch, defaultBranch))
            .Throws<InvalidOperationException>()
            .WithMessageContaining($"Started on {reported};", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(null, false)]
    [Arguments("", false)]
    [Arguments("abc123", true)]
    [Arguments("ABC123", true)]
    public async Task NewOrRetriedReleaseGoesAhead(string? tagged, bool retried)
    {
        await Assert.That(VerifyReleaseModule.CheckTag("v1.2.0", tagged, "abc123"))
            .IsEqualTo(retried ? VerifyReleaseModule.TagState.OnThisCommit : VerifyReleaseModule.TagState.Missing);
    }

    [Test]
    public async Task ReleasedVersionMayNotBeReleasedAgain()
    {
        await Assert.That(() => VerifyReleaseModule.CheckTag("v1.2.0", "def456", "abc123"))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("v1.2.0 already exists on def456", StringComparison.Ordinal);
    }

    [Test]
    public async Task EnvironmentRequestAuthenticatesAndEscapesTheName()
    {
        using var request = VerifyReleaseModule.EnvironmentRequest("BusHero", "GitSnapshotter", "nuget prod/1", "token");

        using (Assert.Multiple())
        {
            await Assert.That(request.Method).IsEqualTo(HttpMethod.Get);
            await Assert.That(request.RequestUri!.IsAbsoluteUri).IsFalse();
            await Assert.That(request.RequestUri.OriginalString)
                .IsEqualTo("repos/BusHero/GitSnapshotter/environments/nuget%20prod%2F1");
            await Assert.That(request.Headers.Authorization!.ToString()).IsEqualTo("Bearer token");
            await Assert.That(request.Headers.Accept.ToString()).IsEqualTo("application/vnd.github+json");
            await Assert.That(request.Headers.UserAgent.ToString()).IsNotEmpty();
        }
    }

    [Test]
    public async Task EnvironmentWithReviewersPasses()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "protection_rules": [ { "type": "required_reviewers", "reviewers": [ {} ] } ] }"""),
        };

        await Assert.That(await VerifyReleaseModule.RequiredReviewersAsync(response, "nuget", CancellationToken.None))
            .IsEqualTo(1);
    }

    [Test]
    public async Task EnvironmentWithoutReviewersFails()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "name": "nuget" }""") };

        await Assert.That(async () => await VerifyReleaseModule.RequiredReviewersAsync(response, "nuget", CancellationToken.None))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("'nuget' environment has no required reviewers", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(HttpStatusCode.NotFound)]
    [Arguments(HttpStatusCode.Unauthorized)]
    [Arguments(HttpStatusCode.Forbidden)]
    [Arguments(HttpStatusCode.Moved)]
    [Arguments(HttpStatusCode.InternalServerError)]
    public async Task UnreadableEnvironmentFails(HttpStatusCode status)
    {
        // A token that can't read the environment must not look like an environment without protection to skip.
        using var response = new HttpResponseMessage(status)
        {
            Content = new StringContent("""{ "protection_rules": [ { "type": "required_reviewers", "reviewers": [ {} ] } ] }"""),
        };

        await Assert.That(async () => await VerifyReleaseModule.RequiredReviewersAsync(response, "nuget", CancellationToken.None))
            .Throws<InvalidOperationException>()
            .WithMessageContaining($"Could not read the 'nuget' environment.", StringComparison.Ordinal);
    }
}
