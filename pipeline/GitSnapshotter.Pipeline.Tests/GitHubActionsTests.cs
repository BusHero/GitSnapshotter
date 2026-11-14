using GitSnapshotter.Pipeline.Modules;

using TUnit.Core;

namespace GitSnapshotter.Pipeline.Tests;

// GITHUB_OUTPUT is process-wide, and set when the tests run on GitHub Actions: each test puts it back.
[NotInParallel(nameof(GitHubActions))]
public sealed class GitHubActionsTests : IDisposable
{
    private const string Variable = "GITHUB_OUTPUT";

    private readonly string? _original = Environment.GetEnvironmentVariable(Variable);
    private readonly string _outputFile = Path.Combine(Path.GetTempPath(), $"github-output-{Guid.NewGuid():N}");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Variable, _original);
        File.Delete(_outputFile);
    }

    [Test]
    public async Task OutputsAreAppendedToTheOutputFile()
    {
        await File.WriteAllTextAsync(_outputFile, "earlier=step\n");
        Environment.SetEnvironmentVariable(Variable, _outputFile);

        await GitHubActions.SetOutputAsync("version", "1.2.0", CancellationToken.None);
        await GitHubActions.SetOutputAsync("prerelease", "false", CancellationToken.None);

        await Assert.That(await File.ReadAllTextAsync(_outputFile))
            .IsEqualTo("earlier=step\nversion=1.2.0\nprerelease=false\n");
    }

    [Test]
    public async Task NothingIsWrittenOutsideGitHubActions()
    {
        Environment.SetEnvironmentVariable(Variable, null);

        await GitHubActions.SetOutputAsync("version", "1.2.0", CancellationToken.None);

        await Assert.That(File.Exists(_outputFile)).IsFalse();
    }

    [Test]
    [Arguments("a\nb")]
    [Arguments("value\nother=injected")]
    public async Task MultiLineValuesAreRefused(string value)
    {
        // A newline would let a value set other outputs.
        Environment.SetEnvironmentVariable(Variable, _outputFile);

        await Assert.That(async () => await GitHubActions.SetOutputAsync("tag", value, CancellationToken.None))
            .Throws<ArgumentException>();
        await Assert.That(File.Exists(_outputFile)).IsFalse();
    }

    [Test]
    [Arguments("1.2.0", "version=1.2.0\ntag=v1.2.0\nprerelease=false\n")]
    [Arguments("v1.2.0-rc.1", "version=1.2.0-rc.1\ntag=v1.2.0-rc.1\nprerelease=true\n")]
    public async Task ReleaseVersionOutputsTheVersionTagAndKind(string version, string expected)
    {
        Environment.SetEnvironmentVariable(Variable, _outputFile);

        await ReleaseVersionModule.SetOutputsAsync(ReleaseVersion.Parse(version), CancellationToken.None);

        await Assert.That(await File.ReadAllTextAsync(_outputFile)).IsEqualTo(expected);
    }
}
