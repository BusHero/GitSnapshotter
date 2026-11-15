using Microsoft.Extensions.Configuration;

using TUnit.Core;

namespace GitSnapshotter.Pipeline.Tests;

public sealed class BuildSettingsTests
{
    private static BuildSettings Bind(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => KeyValuePair.Create<string, string?>(v.Key, v.Value)))
            .Build()
            .GetSection(BuildSettings.Section)
            .Get<BuildSettings>() ?? new BuildSettings();

    [Test]
    public async Task DefaultsBuildEverythingWithoutReleasing()
    {
        var settings = new BuildSettings();

        using (Assert.Multiple())
        {
            await Assert.That(settings.Target).IsEqualTo(BuildTarget.All);
            await Assert.That(settings.Configuration).IsEqualTo("Release");
            await Assert.That(settings.Version).IsNull();
            await Assert.That(settings.IsRelease).IsFalse();
            await Assert.That(settings.BuildNumber).IsNull();
            await Assert.That(settings.Commit).IsNull();
            await Assert.That(settings.ReleaseEnvironment).IsEqualTo("nuget");
        }
    }

    [Test]
    [Arguments("1.2.0", "1.2.0")]
    [Arguments("v1.2.0", "1.2.0")]
    [Arguments("  v1.2.0-rc.1\n", "1.2.0-rc.1")]
    public async Task VersionIsTrimmedAndLosesTheV(string input, string expected)
    {
        var settings = new BuildSettings { Version = input };

        using (Assert.Multiple())
        {
            await Assert.That(settings.Version).IsEqualTo(expected);
            await Assert.That(settings.IsRelease).IsTrue();
        }
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task BlankVersionIsNotARelease(string? input)
    {
        // The release workflow passes the version input as is, so a CI build gets an empty string.
        var settings = new BuildSettings { Version = input };

        using (Assert.Multiple())
        {
            await Assert.That(settings.Version).IsNull();
            await Assert.That(settings.IsRelease).IsFalse();
        }
    }

    [Test]
    public async Task SettingsBindFromTheBuildSection()
    {
        var settings = Bind(
            ("Build:Target", "VerifyRelease"),
            ("Build:Configuration", "Debug"),
            ("Build:PublishDirectory", "out"),
            ("Build:Version", "v2.0.0"),
            ("Build:DefaultBranch", "main"),
            ("Build:BuildNumber", "57"),
            ("Build:Commit", "3d509f869118150a7f0cc1d2c727ecc123da5cc4"),
            ("Other:Version", "9.9.9"));

        using (Assert.Multiple())
        {
            await Assert.That(settings.Target).IsEqualTo(BuildTarget.VerifyRelease);
            await Assert.That(settings.Configuration).IsEqualTo("Debug");
            await Assert.That(settings.PublishDirectory).IsEqualTo("out");
            await Assert.That(settings.Version).IsEqualTo("2.0.0");
            await Assert.That(settings.DefaultBranch).IsEqualTo("main");
            await Assert.That(settings.BuildNumber).IsEqualTo("57");
            await Assert.That(settings.Commit).IsEqualTo("3d509f869118150a7f0cc1d2c727ecc123da5cc4");
        }
    }

    [Test]
    [Arguments("test", BuildTarget.Test)]
    [Arguments("Docs", BuildTarget.Docs)]
    public async Task TargetBindsIgnoringCase(string value, BuildTarget expected)
    {
        await Assert.That(Bind(("Build:Target", value)).Target).IsEqualTo(expected);
    }

    [Test]
    public async Task UnknownTargetFails()
    {
        await Assert.That(() => Bind(("Build:Target", "Deploy"))).Throws<InvalidOperationException>();
    }
}
