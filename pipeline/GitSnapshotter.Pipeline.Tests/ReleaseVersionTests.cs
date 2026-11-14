using TUnit.Core;

namespace GitSnapshotter.Pipeline.Tests;

public sealed class ReleaseVersionTests
{
    [Test]
    [Arguments("1.2.0", "1.2.0")]
    [Arguments("v1.2.0", "1.2.0")]
    [Arguments("0.0.0", "0.0.0")]
    [Arguments("10.20.30", "10.20.30")]
    [Arguments("1.2.0-rc.1", "1.2.0-rc.1")]
    [Arguments("v1.2.0-rc.1", "1.2.0-rc.1")]
    [Arguments("1.2.0-alpha-2.x.0", "1.2.0-alpha-2.x.0")]
    public async Task ParseAcceptsSemVer(string input, string expected)
    {
        var release = ReleaseVersion.Parse(input);

        await Assert.That(release.Version).IsEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments("v")]
    [Arguments("1")]
    [Arguments("1.2")]
    [Arguments("1.2.0.0")]
    [Arguments("01.2.0")]
    [Arguments("1.02.0")]
    [Arguments("1.2.00")]
    [Arguments("1.2.0-")]
    [Arguments("1.2.0-rc..1")]
    [Arguments("1.2.0-rc_1")]
    [Arguments("1.2.0+build.5")]
    [Arguments("vv1.2.0")]
    [Arguments("V1.2.0")]
    [Arguments(" 1.2.0")]
    [Arguments("1.2.0 ")]
    [Arguments("1.2.0\n")]
    [Arguments("refs/tags/v1.2.0")]
    public async Task ParseRejectsAnythingElse(string input)
    {
        await Assert.That(() => ReleaseVersion.Parse(input))
            .Throws<ArgumentException>()
            .WithMessageContaining("<major>.<minor>.<patch>[-<prerelease>]", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("1.2.0", "v1.2.0")]
    [Arguments("v1.2.0-rc.1", "v1.2.0-rc.1")]
    public async Task TagIsTheVersionWithAV(string input, string tag)
    {
        await Assert.That(ReleaseVersion.Parse(input).Tag).IsEqualTo(tag);
    }

    [Test]
    [Arguments("1.2.0", false)]
    [Arguments("1.2.0-rc.1", true)]
    [Arguments("1.2.0-preview", true)]
    public async Task IsPrereleaseWhenThereIsALabel(string input, bool prerelease)
    {
        await Assert.That(ReleaseVersion.Parse(input).IsPrerelease).IsEqualTo(prerelease);
    }

    [Test]
    [Arguments("v1.2.0", "1.2.0")]
    [Arguments("1.2.0", "1.2.0")]
    [Arguments("vv1.2.0", "v1.2.0")]
    [Arguments("V1.2.0", "V1.2.0")]
    [Arguments("", "")]
    public async Task NormalizeDropsOneLeadingV(string input, string expected)
    {
        await Assert.That(ReleaseVersion.Normalize(input)).IsEqualTo(expected);
    }
}
