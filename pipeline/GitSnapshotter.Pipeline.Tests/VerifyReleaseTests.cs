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
}
