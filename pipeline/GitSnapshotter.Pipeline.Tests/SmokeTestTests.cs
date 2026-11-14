using System.IO.Compression;
using System.Xml.Linq;

using GitSnapshotter.Pipeline.Modules;

using TUnit.Core;

namespace GitSnapshotter.Pipeline.Tests;

public sealed class SmokeTestTests : IDisposable
{
    private readonly FakeRestore _restore = new();

    private string Package => Path.Combine(_restore.Root, "GitSnapshotter.1.2.0.nupkg");

    public void Dispose() => _restore.Dispose();

    private void WritePackage(params (string Name, string Content)[] entries) => WritePackage(Package, entries);

    private static void WritePackage(string path, params (string Name, string Content)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write(content);
        }
    }

    // Notices for "Product" (FakeRestore's product name), as ThirdPartyNoticesModule writes them.
    private async Task<string> GeneratedNoticesAsync()
    {
        _restore.AddPackage(
            "Package", "1.0.0", """<license type="expression">MIT</license>""", ("runtime", "lib/net8.0/Package.dll"));
        await _restore.WriteNoticesAsync(ThirdPartyNotices.AppAssets);
        return await _restore.ReadNoticesAsync();
    }

    [Test]
    public async Task GeneratedNoticesPass()
    {
        WritePackage(("lib/net8.0/GitSnapshotter.dll", "dll"), (ThirdPartyNotices.FileName, await GeneratedNoticesAsync()));

        await Assert.That(() => SmokeTestModule.CheckNotices(Package, "Product")).ThrowsNothing();
    }

    [Test]
    public async Task PackageWithoutNoticesFails()
    {
        WritePackage(("lib/net8.0/GitSnapshotter.dll", "dll"), ("README.md", "readme"));

        await Assert.That(() => SmokeTestModule.CheckNotices(Package, "Product"))
            .Throws<InvalidOperationException>()
            .WithMessageContaining($"has no {ThirdPartyNotices.FileName}", StringComparison.Ordinal);
    }

    [Test]
    public async Task NoticesOutsideThePackageRootFail()
    {
        WritePackage(($"docs/{ThirdPartyNotices.FileName}", await GeneratedNoticesAsync()));

        await Assert.That(() => SmokeTestModule.CheckNotices(Package, "Product"))
            .Throws<InvalidOperationException>()
            .WithMessageContaining($"has no {ThirdPartyNotices.FileName}", StringComparison.Ordinal);
    }

    [Test]
    public async Task NoticesForAnotherProductFail()
    {
        WritePackage((ThirdPartyNotices.FileName, await GeneratedNoticesAsync()));

        await Assert.That(() => SmokeTestModule.CheckNotices(Package, ThirdPartyNoticesModule.LibraryProduct))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("is not the notices for", StringComparison.Ordinal);
    }

    [Test]
    public async Task NoticesListingNoPackagesFail()
    {
        WritePackage((ThirdPartyNotices.FileName, ThirdPartyNotices.Heading("Product") + "\n"));

        await Assert.That(() => SmokeTestModule.CheckNotices(Package, "Product"))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("lists no packages", StringComparison.Ordinal);
    }

    [Test]
    public async Task DownloadedPackageIsFoundWhateverItsCase()
    {
        // A tool path's store: .store/<id>/<version>/<id>/<version>/<id>.<version>.nupkg, in lower case.
        var directory = Path.Combine(_restore.Root, "tools", ".store", "gitsnapshotter.tool", "1.2.0-rc.1", "gitsnapshotter.tool", "1.2.0-rc.1");
        Directory.CreateDirectory(directory);
        var package = Path.Combine(directory, "gitsnapshotter.tool.1.2.0-rc.1.nupkg");
        await File.WriteAllTextAsync(package, "package");

        await Assert.That(SmokeTestModule.FindPackage(Path.Combine(_restore.Root, "tools"), "GitSnapshotter.Tool", "1.2.0-rc.1"))
            .IsEqualTo(package);
    }

    [Test]
    public async Task PackageAtAnotherVersionIsNotFound()
    {
        Directory.CreateDirectory(_restore.Root);
        await File.WriteAllTextAsync(Path.Combine(_restore.Root, "gitsnapshotter.1.1.0.nupkg"), "package");

        await Assert.That(() => SmokeTestModule.FindPackage(_restore.Root, "GitSnapshotter", "1.2.0"))
            .Throws<FileNotFoundException>();
    }

    [Test]
    [Arguments("1.2.0\n")]
    [Arguments("1.2.0+5be4604abcdef\n")]
    public async Task ToolReportingTheVersionPasses(string output)
    {
        await Assert.That(() => SmokeTestModule.CheckToolVersion(output, "1.2.0")).ThrowsNothing();
    }

    [Test]
    [Arguments("1.2.0-rc.1\n")]
    [Arguments("1.2.01\n")]
    [Arguments("1.1.0+5be4604\n")]
    [Arguments("")]
    public async Task ToolReportingAnotherVersionFails(string output)
    {
        await Assert.That(() => SmokeTestModule.CheckToolVersion(output, "1.2.0"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task NuGetConfigUsesOnlyTheGivenFeed()
    {
        var sources = XDocument.Parse(SmokeTestModule.NuGetConfig("https://example.test/v3/index.json?a=1&b=2"))
            .Root!.Element("packageSources")!.Elements().ToList();

        using (Assert.Multiple())
        {
            // <clear/> first: the user's and the machine's feeds are not used.
            await Assert.That(sources.Select(source => source.Name.LocalName)).IsEquivalentTo(["clear", "add"]);
            await Assert.That(sources[1].Attribute("value")!.Value).IsEqualTo("https://example.test/v3/index.json?a=1&b=2");
        }
    }

    [Test]
    public async Task LibraryProjectReferencesExactlyTheVersion()
    {
        var project = XDocument.Parse(SmokeTestModule.LibraryProject("1.2.0", "net11.0")).Root!;
        var reference = project.Descendants("PackageReference").Single();

        using (Assert.Multiple())
        {
            await Assert.That(reference.Attribute("Include")!.Value).IsEqualTo(SmokeTestModule.LibraryId);
            await Assert.That(reference.Attribute("Version")!.Value).IsEqualTo("[1.2.0]");
            await Assert.That(project.Descendants("TargetFramework").Single().Value).IsEqualTo("net11.0");
        }
    }

    private string Released => Path.Combine(_restore.Root, "release", "GitSnapshotter.1.2.0.nupkg");

    private string Downloaded => Path.Combine(_restore.Root, "packages", "gitsnapshotter.1.2.0.nupkg");

    private static readonly (string, string)[] Files =
        [("GitSnapshotter.nuspec", "<package/>"), ("lib/net8.0/GitSnapshotter.dll", "dll")];

    [Test]
    public async Task SignedCopyOfTheReleasedPackageMatches()
    {
        WritePackage(Released, Files);
        // nuget.org adds its repository signature; the order of the files doesn't matter either.
        WritePackage(Downloaded, [(SmokeTestModule.RepositorySignature, "signature"), .. Files.Reverse()]);

        await Assert.That(() => SmokeTestModule.ComparePackages(Downloaded, Released)).ThrowsNothing();
    }

    [Test]
    public async Task PackageWithADifferentFileDoesNotMatch()
    {
        WritePackage(Released, Files);
        WritePackage(Downloaded, ("GitSnapshotter.nuspec", "<package/>"), ("lib/net8.0/GitSnapshotter.dll", "other dll"));

        await Assert.That(() => SmokeTestModule.ComparePackages(Downloaded, Released))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("lib/net8.0/GitSnapshotter.dll differs", StringComparison.Ordinal);
    }

    [Test]
    public async Task PackageWithMissingOrExtraFilesDoesNotMatch()
    {
        WritePackage(Released, Files);
        WritePackage(Downloaded, ("GitSnapshotter.nuspec", "<package/>"), ("lib/net8.0/Extra.dll", "dll"));

        await Assert.That(() => SmokeTestModule.ComparePackages(Downloaded, Released))
            .Throws<InvalidOperationException>()
            .WithMessageContaining(
                "lib/net8.0/Extra.dll is extra; lib/net8.0/GitSnapshotter.dll is missing", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReleaseWithoutThePackageFails()
    {
        WritePackage(Downloaded, Files);

        await Assert.That(() => SmokeTestModule.ComparePackages(Downloaded, Released)).Throws<FileNotFoundException>();
    }
}
