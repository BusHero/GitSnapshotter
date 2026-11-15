using TUnit.Core;

namespace GitSnapshotter.Pipeline.Tests;

public sealed class PackageVersionTests : IDisposable
{
    private readonly string _props = Path.Combine(Path.GetTempPath(), $"Directory.Build.{Guid.NewGuid():N}.props");

    public void Dispose() => File.Delete(_props);

    private static ReleaseVersion Next(string prefix, params string[] tags) =>
        PackageVersion.Next(ReleaseVersion.Parse(prefix), tags);

    [Test]
    [Arguments("57", "57")]
    [Arguments(" 57\n", "57")]
    [Arguments(null, "local")]
    [Arguments("", "local")]
    [Arguments("   ", "local")]
    [Arguments("57.2", "57.2")]
    public async Task BuildIsTheRunNumberOrLocal(string? buildNumber, string expected)
    {
        await Assert.That(PackageVersion.Build(buildNumber)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("57+1")]
    [Arguments("a/b")]
    [Arguments("57.")]
    [Arguments("..")]
    public async Task BuildMustBeValidBuildMetadata(string buildNumber)
    {
        await Assert.That(() => PackageVersion.Build(buildNumber)).Throws<ArgumentException>();
    }

    [Test]
    public async Task FullVersionAddsTheBuildAsMetadata()
    {
        var version = new BuildVersion(ReleaseVersion.Parse("0.4.1"), "57");

        using (Assert.Multiple())
        {
            await Assert.That(version.Version).IsEqualTo("0.4.1");
            await Assert.That(version.Full).IsEqualTo("0.4.1+57");
        }
    }

    [Test]
    // Nothing released at or above the prefix: the prefix.
    [Arguments("0.4.0", new string[0], "0.4.0")]
    [Arguments("0.4.0", new[] { "v0.1.0", "v0.3.0" }, "0.4.0")]
    // Its prereleases don't count: 0.4.0 comes after 0.4.0-rc.1.
    [Arguments("0.4.0", new[] { "v0.3.0", "v0.4.0-rc.1" }, "0.4.0")]
    // Released: the patch after the newest release, so nobody has to raise the prefix after each one.
    [Arguments("0.4.0", new[] { "v0.3.0", "v0.4.0" }, "0.4.1")]
    [Arguments("0.4.0", new[] { "v0.4.0", "v0.4.1", "v0.4.2" }, "0.4.3")]
    [Arguments("0.4.0", new[] { "v0.4.2", "v0.4.10", "v0.4.9" }, "0.4.11")]
    // A raised prefix starts a new minor or major version.
    [Arguments("0.5.0", new[] { "v0.4.0", "v0.4.1" }, "0.5.0")]
    [Arguments("1.0.0", new[] { "v0.4.1" }, "1.0.0")]
    // A prefix left below a newer release still builds above it.
    [Arguments("0.4.0", new[] { "v0.5.0" }, "0.5.1")]
    // A prerelease prefix that isn't released yet.
    [Arguments("0.5.0-rc.1", new[] { "v0.4.1" }, "0.5.0-rc.1")]
    [Arguments("0.5.0-rc.2", new[] { "v0.4.1", "v0.5.0-rc.1" }, "0.5.0-rc.2")]
    // Tags that aren't releases are ignored.
    [Arguments("0.4.0", new[] { "0.4.0", "v0.4.0-", "vnext", "release-0.4.0", "v0.4" }, "0.4.0")]
    public async Task NextReleaseIsThePrefixOrThePatchAfterTheNewestRelease(string prefix, string[] tags, string expected)
    {
        await Assert.That(Next(prefix, tags).Version).IsEqualTo(expected);
    }

    [Test]
    [Arguments("0.5.0-rc.1", new[] { "v0.5.0-rc.1" }, "v0.5.0-rc.1")]
    [Arguments("0.5.0-rc.1", new[] { "v0.5.0-rc.2" }, "v0.5.0-rc.2")]
    [Arguments("0.5.0-rc.1", new[] { "v0.5.0" }, "v0.5.0")]
    public async Task ReleasedPrereleasePrefixMustBeRaised(string prefix, string[] tags, string release)
    {
        // Only a person can pick the next label (rc.2, or the release).
        await Assert.That(() => Next(prefix, tags))
            .Throws<InvalidOperationException>()
            .WithMessageContaining(release[1..], StringComparison.Ordinal);
    }

    [Test]
    public async Task PrefixIsReadFromTheProps()
    {
        await File.WriteAllTextAsync(_props, """
            <Project>
              <PropertyGroup>
                <VersionPrefix> 0.4.0 </VersionPrefix>
                <Authors>BusHero</Authors>
              </PropertyGroup>
            </Project>
            """);

        await Assert.That(PackageVersion.ReadPrefix(_props).Version).IsEqualTo("0.4.0");
    }

    [Test]
    public async Task PropsWithoutAPrefixFail()
    {
        await File.WriteAllTextAsync(_props, "<Project><PropertyGroup /></Project>");

        await Assert.That(() => PackageVersion.ReadPrefix(_props))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("VersionPrefix", StringComparison.Ordinal);
    }

    [Test]
    public async Task TheRepositorysPrefixIsAVersion()
    {
        var props = Path.Combine(Repository.Root, "Source", "Directory.Build.props");

        await Assert.That(() => PackageVersion.ReadPrefix(props)).ThrowsNothing();
    }
}
