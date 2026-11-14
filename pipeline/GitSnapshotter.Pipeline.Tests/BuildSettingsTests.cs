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
            await Assert.That(settings.VersionProperties).IsEmpty();
            await Assert.That(settings.NuGetApiKey).IsNull();
            await Assert.That(settings.ReleaseEnvironment).IsEqualTo("nuget");
            await Assert.That(settings.SmokeTestSource).IsEqualTo("https://api.nuget.org/v3/index.json");
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
            await Assert.That(settings.VersionProperties).IsEmpty();
        }
    }

    [Test]
    public async Task ReleaseStampsTheVersionProperty()
    {
        var settings = new BuildSettings { Version = "v1.2.0" };

        var property = await Assert.That(settings.VersionProperties).HasSingleItem();

        using (Assert.Multiple())
        {
            await Assert.That(property!.Key).IsEqualTo("Version");
            await Assert.That(property.Value).IsEqualTo("1.2.0");
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
            ("Other:Version", "9.9.9"));

        using (Assert.Multiple())
        {
            await Assert.That(settings.Target).IsEqualTo(BuildTarget.VerifyRelease);
            await Assert.That(settings.Configuration).IsEqualTo("Debug");
            await Assert.That(settings.PublishDirectory).IsEqualTo("out");
            await Assert.That(settings.Version).IsEqualTo("2.0.0");
            await Assert.That(settings.DefaultBranch).IsEqualTo("main");
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
