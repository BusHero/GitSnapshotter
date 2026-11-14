using System.IO.Compression;
using System.Security.Cryptography;

using GitSnapshotter.Pipeline.Modules;

using TUnit.Core;

namespace GitSnapshotter.Pipeline.Tests;

public sealed class ReleaseModulesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pipeline-tests-{Guid.NewGuid():N}");

    private string Publish => Path.Combine(_root, "publish");

    private string Release => Path.Combine(_root, "release");

    public ReleaseModulesTests() => Directory.CreateDirectory(Publish);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(Publish, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void WritePublishDirectory(string version)
    {
        Write($"GitSnapshotter.{version}.nupkg", "library");
        Write($"GitSnapshotter.{version}.snupkg", "library symbols");
        Write($"GitSnapshotter.Tool.{version}.nupkg", "tool");
        Write($"GitSnapshotter.Tool.{version}.snupkg", "tool symbols");
        Write("gitsnapshotter", "app host");
        Write("GitSnapshotter.dll", "app");
        Write(ThirdPartyNotices.FileName, "notices");
        Write(Path.Combine("runtimes", "linux-x64", "native", "libgit2.so"), "native");
    }

    [Test]
    public async Task ReleaseGetsThePackagesTheZipAndChecksums()
    {
        WritePublishDirectory("1.2.0");

        var files = await ReleaseAssetsModule.CreateAsync(Publish, Release, "1.2.0", CancellationToken.None);

        await Assert.That(files.Select(file => Path.GetFileName(file))).IsEquivalentTo(
        [
            "GitSnapshotter.1.2.0.nupkg",
            "GitSnapshotter.1.2.0.snupkg",
            "GitSnapshotter.Console_1.2.0.zip",
            "GitSnapshotter.Tool.1.2.0.nupkg",
            "GitSnapshotter.Tool.1.2.0.snupkg",
            ReleaseAssetsModule.ChecksumsFile,
        ]);
    }

    [Test]
    public async Task ZipHoldsTheConsoleAppWithoutThePackages()
    {
        WritePublishDirectory("1.2.0");

        await ReleaseAssetsModule.CreateAsync(Publish, Release, "1.2.0", CancellationToken.None);

        using var zip = await ZipFile.OpenReadAsync(Path.Combine(Release, "GitSnapshotter.Console_1.2.0.zip"));
        await Assert.That(zip.Entries.Select(entry => entry.FullName)).IsEquivalentTo(
        [
            "GitSnapshotter.dll",
            ThirdPartyNotices.FileName,
            "gitsnapshotter",
            "runtimes/linux-x64/native/libgit2.so",
        ]);
    }

    [Test]
    public async Task ChecksumsCoverEveryOtherFileInSha256SumFormat()
    {
        WritePublishDirectory("1.2.0");

        await ReleaseAssetsModule.CreateAsync(Publish, Release, "1.2.0", CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(Path.Combine(Release, ReleaseAssetsModule.ChecksumsFile));
        var others = Directory.EnumerateFiles(Release)
            .Select(file => Path.GetFileName(file))
            .Where(name => name != ReleaseAssetsModule.ChecksumsFile)
            .Order(StringComparer.Ordinal)
            .ToList();

        await Assert.That(lines.Select(line => line[66..])).IsEquivalentTo(others);
        foreach (var line in lines)
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(Release, line[66..]))));
            await Assert.That(line).IsEqualTo($"{hash}  {line[66..]}");
        }
    }

    [Test]
    public async Task EarlierReleaseFilesAreRemoved()
    {
        WritePublishDirectory("1.2.0");
        Directory.CreateDirectory(Release);
        await File.WriteAllTextAsync(Path.Combine(Release, "GitSnapshotter.1.1.0.nupkg"), "old");

        var files = await ReleaseAssetsModule.CreateAsync(Publish, Release, "1.2.0", CancellationToken.None);

        await Assert.That(files.Select(file => Path.GetFileName(file))).DoesNotContain("GitSnapshotter.1.1.0.nupkg");
    }

    [Test]
    [Arguments("GitSnapshotter.1.2.0.nupkg")]
    [Arguments("GitSnapshotter.1.2.0.nupkg GitSnapshotter.Tool.1.2.0-trunk-20261114090507.nupkg")]
    [Arguments("GitSnapshotter.1.2.0.nupkg GitSnapshotter.Tool.1.2.0.nupkg Other.1.2.0.nupkg")]
    public async Task OnlyTheExpectedPackagesAtTheReleaseVersionAreReleased(string packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        foreach (var package in packages.Split(' '))
        {
            Write(package, "package");
        }

        await Assert.That(async () => await ReleaseAssetsModule.CreateAsync(Publish, Release, "1.2.0", CancellationToken.None))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Expected exactly: GitSnapshotter.1.2.0.nupkg GitSnapshotter.Tool.1.2.0.nupkg", StringComparison.Ordinal);
        await Assert.That(Directory.Exists(Release)).IsFalse();
    }

    [Test]
    public async Task ReleaseNotesAreWrittenFromTheChangelog()
    {
        var changelog = Path.Combine(_root, Changelog.FileName);
        await File.WriteAllLinesAsync(changelog, ["## [Unreleased]", "", "- Next.", "", "## [1.2.0]", "", "- Shipped."]);
        var directory = Path.Combine(_root, "notes", "nested");

        var (section, notes, file) = await ReleaseNotesModule.WriteAsync(
            changelog, ReleaseVersion.Parse("1.3.0-rc.1"), directory, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(section).IsEqualTo(Changelog.Unreleased);
            await Assert.That(notes).IsEqualTo("- Next.");
            await Assert.That(file).IsEqualTo(Path.Combine(directory, ReleaseNotesModule.FileName));
            await Assert.That(await File.ReadAllBytesAsync(file)).IsEquivalentTo("- Next.\n"u8.ToArray());
        }
    }

    [Test]
    public async Task ReleaseNotesAreNotWrittenWhenThereAreNone()
    {
        var changelog = Path.Combine(_root, Changelog.FileName);
        await File.WriteAllLinesAsync(changelog, ["## [Unreleased]", "", "- Next."]);
        var directory = Path.Combine(_root, "notes");

        await Assert.That(async () => await ReleaseNotesModule.WriteAsync(
                changelog, ReleaseVersion.Parse("1.3.0"), directory, CancellationToken.None))
            .Throws<InvalidOperationException>();
        await Assert.That(Directory.Exists(directory)).IsFalse();
    }
}
