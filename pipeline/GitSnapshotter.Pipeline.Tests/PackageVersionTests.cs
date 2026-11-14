using TUnit.Core;

namespace GitSnapshotter.Pipeline.Tests;

public sealed class PackageVersionTests
{
    private static readonly DateTime BuildTime = new(2026, 11, 14, 9, 5, 7, DateTimeKind.Utc);

    [Test]
    [Arguments("trunk", "trunk-20261114090507")]
    [Arguments("claude/fix-it", "claude-fix-it-20261114090507")]
    [Arguments("a/b/c", "a-b-c-20261114090507")]
    [Arguments(null, "local-20261114090507")]
    [Arguments("", "local-20261114090507")]
    public async Task SuffixIsTheBranchAndTheBuildTime(string? branch, string expected)
    {
        await Assert.That(PackageVersion.Suffix(new BuildSettings(), branch, BuildTime)).IsEqualTo(expected);
    }

    [Test]
    public async Task ReleaseHasNoSuffix()
    {
        await Assert.That(PackageVersion.Suffix(new BuildSettings { Version = "1.2.0" }, "trunk", BuildTime)).IsNull();
    }

    [Test]
    [Arguments("trunk", "trunk", null, "20261114090507+trunk")]
    [Arguments("feature", "trunk", null, "20261114090507+merge")]
    [Arguments("main", "trunk", "main", "20261114090507+trunk")]
    [Arguments("trunk", "trunk", "main", "20261114090507+merge")]
    public async Task ConsoleVersionSaysWhetherTheDefaultBranchWasBuilt(
        string branch, string detectedDefault, string? configuredDefault, string expected)
    {
        var settings = new BuildSettings { DefaultBranch = configuredDefault };

        await Assert.That(PackageVersion.Console(settings, branch, detectedDefault, BuildTime)).IsEqualTo(expected);
    }

    [Test]
    public async Task ReleaseConsoleVersionIsTheReleaseVersion()
    {
        var settings = new BuildSettings { Version = "v1.2.0-rc.1" };

        await Assert.That(PackageVersion.Console(settings, "trunk", "trunk", BuildTime)).IsEqualTo("1.2.0-rc.1");
    }
}
