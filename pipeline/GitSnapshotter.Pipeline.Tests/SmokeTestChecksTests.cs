using System.IO.Compression;
using System.Xml.Linq;

using GitSnapshotter.Pipeline.Modules;
using GitSnapshotter.SmokeTests;

using TUnit.Assertions.Enums;

using TUnit.Core;

namespace GitSnapshotter.Pipeline.Tests;

public sealed class SmokeTestChecksTests : IDisposable
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

        await Assert.That(() => SmokeTestChecks.CheckNotices(Package, "Product")).ThrowsNothing();
    }

    [Test]
    public async Task PackageWithoutNoticesFails()
    {
        WritePackage(("lib/net8.0/GitSnapshotter.dll", "dll"), ("README.md", "readme"));

        await Assert.That(() => SmokeTestChecks.CheckNotices(Package, "Product"))
            .Throws<InvalidOperationException>()
            .WithMessageContaining($"has no {ThirdPartyNotices.FileName}", StringComparison.Ordinal);
    }

    [Test]
    public async Task NoticesOutsideThePackageRootFail()
    {
        WritePackage(($"docs/{ThirdPartyNotices.FileName}", await GeneratedNoticesAsync()));

        await Assert.That(() => SmokeTestChecks.CheckNotices(Package, "Product"))
            .Throws<InvalidOperationException>()
            .WithMessageContaining($"has no {ThirdPartyNotices.FileName}", StringComparison.Ordinal);
    }

    [Test]
    public async Task NoticesForAnotherProductFail()
    {
        WritePackage((ThirdPartyNotices.FileName, await GeneratedNoticesAsync()));

        await Assert.That(() => SmokeTestChecks.CheckNotices(Package, ThirdPartyNotices.LibraryProduct))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("is not the notices for", StringComparison.Ordinal);
    }

    [Test]
    public async Task NoticesListingNoPackagesFail()
    {
        WritePackage((ThirdPartyNotices.FileName, ThirdPartyNotices.Heading("Product") + "\n"));

        await Assert.That(() => SmokeTestChecks.CheckNotices(Package, "Product"))
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

        await Assert.That(SmokeTestChecks.FindPackage(Path.Combine(_restore.Root, "tools"), "GitSnapshotter.Tool", "1.2.0-rc.1"))
            .IsEqualTo(package);
    }

    [Test]
    public async Task PackageAtAnotherVersionIsNotFound()
    {
        Directory.CreateDirectory(_restore.Root);
        await File.WriteAllTextAsync(Path.Combine(_restore.Root, "gitsnapshotter.1.1.0.nupkg"), "package");

        await Assert.That(() => SmokeTestChecks.FindPackage(_restore.Root, "GitSnapshotter", "1.2.0"))
            .Throws<FileNotFoundException>();
    }

    [Test]
    [Arguments("1.2.0\n")]
    [Arguments("1.2.0+5be4604abcdef\n")]
    public async Task ToolReportingTheVersionPasses(string output)
    {
        await Assert.That(() => SmokeTestChecks.CheckToolVersion(output, "1.2.0")).ThrowsNothing();
    }

    [Test]
    [Arguments("1.2.0-rc.1\n")]
    [Arguments("1.2.01\n")]
    [Arguments("1.1.0+5be4604\n")]
    [Arguments("")]
    public async Task ToolReportingAnotherVersionFails(string output)
    {
        await Assert.That(() => SmokeTestChecks.CheckToolVersion(output, "1.2.0"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task NuGetConfigUsesOnlyTheGivenFeed()
    {
        var sources = XDocument.Parse(SmokeTestChecks.NuGetConfig("https://example.test/v3/index.json?a=1&b=2"))
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
        var project = XDocument.Parse(SmokeTestChecks.LibraryProject("1.2.0", ["net11.0"])).Root!;
        var reference = project.Descendants("PackageReference").Single();

        using (Assert.Multiple())
        {
            await Assert.That(reference.Attribute("Include")!.Value).IsEqualTo(SmokeTestChecks.LibraryId);
            await Assert.That(reference.Attribute("Version")!.Value).IsEqualTo("[1.2.0]");
            await Assert.That(project.Descendants("TargetFrameworks").Single().Value).IsEqualTo("net11.0");
            await Assert.That(project.Descendants("PackageReference").Count()).IsEqualTo(1);
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
        WritePackage(Downloaded, [(SmokeTestChecks.RepositorySignature, "signature"), .. Files.Reverse()]);

        await Assert.That(() => SmokeTestChecks.ComparePackages(Downloaded, Released)).ThrowsNothing();
    }

    [Test]
    public async Task PackageWithADifferentFileDoesNotMatch()
    {
        WritePackage(Released, Files);
        WritePackage(Downloaded, ("GitSnapshotter.nuspec", "<package/>"), ("lib/net8.0/GitSnapshotter.dll", "other dll"));

        await Assert.That(() => SmokeTestChecks.ComparePackages(Downloaded, Released))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("lib/net8.0/GitSnapshotter.dll differs", StringComparison.Ordinal);
    }

    [Test]
    public async Task PackageWithMissingOrExtraFilesDoesNotMatch()
    {
        WritePackage(Released, Files);
        WritePackage(Downloaded, ("GitSnapshotter.nuspec", "<package/>"), ("lib/net8.0/Extra.dll", "dll"));

        await Assert.That(() => SmokeTestChecks.ComparePackages(Downloaded, Released))
            .Throws<InvalidOperationException>()
            .WithMessageContaining(
                "lib/net8.0/Extra.dll is extra; lib/net8.0/GitSnapshotter.dll is missing", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReleaseWithoutThePackageFails()
    {
        WritePackage(Downloaded, Files);

        await Assert.That(() => SmokeTestChecks.ComparePackages(Downloaded, Released)).Throws<FileNotFoundException>();
    }

    [Test]
    public async Task NetFrameworkAppAlsoGetsTheReferenceAssemblies()
    {
        var project = XDocument.Parse(SmokeTestChecks.LibraryProject("1.2.0", ["net11.0", "net472"])).Root!;

        using (Assert.Multiple())
        {
            await Assert.That(project.Descendants("TargetFrameworks").Single().Value).IsEqualTo("net11.0;net472");
            await Assert.That(project.Descendants("PackageReference").Select(r => r.Attribute("Include")!.Value))
                .IsEquivalentTo([SmokeTestChecks.LibraryId, "Microsoft.NETFramework.ReferenceAssemblies"]);
        }
    }

    [Test]
    [Arguments(true, new[] { "net11.0", "net472", "net8.0" })]
    [Arguments(false, new[] { "net11.0", "net8.0" })]
    public async Task LibraryRunsOnEachFrameworkItIsBuiltForAndTheNewest(bool windows, string[] expected)
    {
        await Assert.That(SmokeTestChecks.TestFrameworks(["net8.0", "net472"], "net11.0", windows))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("net472", true)]
    [Arguments("net48", true)]
    [Arguments("net8.0", false)]
    [Arguments("netstandard2.0", false)]
    [Arguments("net11.0", false)]
    public async Task NetFrameworkIsRecognised(string framework, bool expected)
    {
        await Assert.That(SmokeTestChecks.IsNetFramework(framework)).IsEqualTo(expected);
    }

    // ---- Contents -------------------------------------------------------------------------------------------------

    private static readonly XDocument LibraryCsproj = XDocument.Parse("""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><TargetFrameworks>net8.0;net472</TargetFrameworks></PropertyGroup>
          <ItemGroup>
            <PackageReference Include="LibGit2Sharp" />
            <PackageReference Include="Microsoft.CodeAnalysis.PublicApiAnalyzers" PrivateAssets="all" />
          </ItemGroup>
          <ItemGroup Condition="'$(TargetFramework)' == 'net472'">
            <PackageReference Include="Polyfill"><PrivateAssets>all</PrivateAssets></PackageReference>
            <PackageReference Include="System.Text.Json" />
          </ItemGroup>
        </Project>
        """);

    private static string Nuspec(string id, params string[] dependencies) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata>
            <id>{id}</id>
            <dependencies><group targetFramework="net8.0">{string.Concat(dependencies.Select(d => $"<dependency id=\"{d}\" version=\"1.0.0\" />"))}</group></dependencies>
          </metadata>
        </package>
        """;

    private static Dictionary<string, byte[]> Entries(params (string Name, string Content)[] entries) =>
        entries.ToDictionary(e => e.Name, e => System.Text.Encoding.UTF8.GetBytes(e.Content));

    // The library package 0.2.0 as published, with nuget.org's signature.
    private static (string, string)[] LibraryFiles(params string[] dependencies) =>
    [
        ("_rels/.rels", ""), ("[Content_Types].xml", ""), ("package/services/metadata/core-properties/abc.psmdcp", ""),
        (".signature.p7s", ""), ("GitSnapshotter.nuspec", Nuspec("GitSnapshotter", dependencies)),
        ("README.md", ""), ("icon.png", ""), (ThirdPartyNotices.FileName, ""),
        ("lib/net472/GitSnapshotter.dll", ""), ("lib/net472/GitSnapshotter.xml", ""),
        ("lib/net8.0/GitSnapshotter.dll", ""), ("lib/net8.0/GitSnapshotter.xml", ""),
    ];

    [Test]
    public async Task LibraryPackageAsPublishedPasses()
    {
        var entries = Entries(LibraryFiles("LibGit2Sharp", "System.Text.Json"));

        await Assert.That(() => SmokeTestChecks.CheckLibraryContents(entries, LibraryCsproj)).ThrowsNothing();
    }

    [Test]
    [Arguments("lib/net8.0/Constants.dll", "lib/net8.0/Constants.dll is not expected")]
    [Arguments("lib/netstandard2.0/GitSnapshotter.dll", "lib/netstandard2.0/GitSnapshotter.dll is not expected")]
    [Arguments("lib/net8.0/GitSnapshotter.pdb", "lib/net8.0/GitSnapshotter.pdb is not expected")]
    [Arguments("appsettings.json", "appsettings.json is not expected")]
    public async Task LibraryPackageWithAnExtraFileFails(string extra, string message)
    {
        var entries = Entries([.. LibraryFiles("LibGit2Sharp"), (extra, "")]);

        await Assert.That(() => SmokeTestChecks.CheckLibraryContents(entries, LibraryCsproj))
            .Throws<InvalidOperationException>()
            .WithMessageContaining(message, StringComparison.Ordinal);
    }

    [Test]
    public async Task LibraryPackageMissingAFrameworkFails()
    {
        var entries = Entries([.. LibraryFiles("LibGit2Sharp").Where(e => !e.Item1.StartsWith("lib/net472/", StringComparison.Ordinal))]);

        await Assert.That(() => SmokeTestChecks.CheckLibraryContents(entries, LibraryCsproj))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("lib/net472/GitSnapshotter.dll is missing", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("Polyfill")]
    [Arguments("Microsoft.CodeAnalysis.PublicApiAnalyzers")]
    [Arguments("TUnit.Engine")]
    public async Task LibraryDependingOnAPrivatePackageFails(string dependency)
    {
        var entries = Entries(LibraryFiles("LibGit2Sharp", dependency));

        await Assert.That(() => SmokeTestChecks.CheckLibraryContents(entries, LibraryCsproj))
            .Throws<InvalidOperationException>()
            .WithMessageContaining($"it depends on {dependency}", StringComparison.Ordinal);
    }

    [Test]
    public async Task PublicPackageReferencesLeaveOutPrivateAssets()
    {
        await Assert.That(SmokeTestChecks.PublicPackageReferences(LibraryCsproj)).IsEquivalentTo(["LibGit2Sharp", "System.Text.Json"]);
    }

    private static readonly Dictionary<string, string> ToolNoticed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LibGit2Sharp"] = "0.32.0",
        ["LibGit2Sharp.NativeBinaries"] = "2.0.323",
        ["System.CommandLine"] = "2.0.14",
    };

    private static (string, string)[] ToolFiles() =>
    [
        ("_rels/.rels", ""), ("[Content_Types].xml", ""), ("package/services/metadata/core-properties/abc.psmdcp", ""),
        ("GitSnapshotter.Tool.nuspec", Nuspec("GitSnapshotter.Tool")), ("README.md", ""), ("icon.png", ""),
        (ThirdPartyNotices.FileName, ""),
        ("tools/net11.0/any/DotnetToolSettings.xml", ""),
        ("tools/net11.0/any/GitSnapshotter.Console.deps.json", ""),
        ("tools/net11.0/any/GitSnapshotter.Console.dll", ""),
        ("tools/net11.0/any/GitSnapshotter.Console.pdb", ""),
        ("tools/net11.0/any/GitSnapshotter.Console.runtimeconfig.json", ""),
        ("tools/net11.0/any/GitSnapshotter.dll", ""),
        ("tools/net11.0/any/GitSnapshotter.xml", ""),
        ("tools/net11.0/any/LibGit2Sharp.dll", ""),
        ("tools/net11.0/any/System.CommandLine.dll", ""),
        ("tools/net11.0/any/de/System.CommandLine.resources.dll", ""),
        ("tools/net11.0/any/runtimes/linux-x64/native/libgit2-5853918.so", ""),
        ("tools/net11.0/any/runtimes/win-x64/native/git2-5853918.dll", ""),
    ];

    [Test]
    public async Task ToolPackageAsPublishedPasses()
    {
        await Assert.That(() => SmokeTestChecks.CheckToolContents(Entries(ToolFiles()), ToolNoticed)).ThrowsNothing();
    }

    [Test]
    [Arguments("tools/net11.0/any/TUnit.Core.dll")]
    [Arguments("tools/net11.0/any/Constants.dll")]
    [Arguments("tools/net11.0/any/GitSnapshotter.UnitTests.dll")]
    [Arguments("tools/net11.0/any/fr/TUnit.Core.resources.dll")]
    [Arguments("tools/net11.0/any/appsettings.Development.json")]
    [Arguments("tools/net11.0/GitSnapshotter.Console.dll")]
    [Arguments("lib/net8.0/GitSnapshotter.dll")]
    public async Task ToolPackageWithAFileNoNoticeCoversFails(string extra)
    {
        var entries = Entries([.. ToolFiles(), (extra, "")]);

        await Assert.That(() => SmokeTestChecks.CheckToolContents(entries, ToolNoticed))
            .Throws<InvalidOperationException>()
            .WithMessageContaining($"{extra} is not expected", StringComparison.Ordinal);
    }

    [Test]
    public async Task ToolPackageWithDependenciesFails()
    {
        var entries = Entries([.. ToolFiles().Where(e => !e.Item1.EndsWith(".nuspec", StringComparison.Ordinal)),
            ("GitSnapshotter.Tool.nuspec", Nuspec("GitSnapshotter.Tool", "LibGit2Sharp"))]);

        await Assert.That(() => SmokeTestChecks.CheckToolContents(entries, ToolNoticed))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("it depends on LibGit2Sharp", StringComparison.Ordinal);
    }

    [Test]
    public async Task NoticesListTheirPackages()
    {
        var noticed = SmokeTestChecks.CheckNotices(await GeneratedNoticesAsync(), "Product", "test");

        await Assert.That(noticed).IsEquivalentTo(new Dictionary<string, string> { ["Package"] = "1.0.0" });
    }

    // ---- What nuget.org shows -------------------------------------------------------------------------------------

    private const string Commit = "8bdbf86021adf5f344d3323cefe41d7b09e375d1";

    private static readonly System.Xml.Linq.XElement PackageMetadata = XDocument.Parse($"""
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata>
            <id>GitSnapshotter</id>
            <version>0.2.0</version>
            <authors>BusHero</authors>
            <license type="expression">MIT</license>
            <icon>icon.png</icon>
            <readme>README.md</readme>
            <description>Take a snapshot.</description>
            <repository type="git" url="https://github.com/BusHero/GitSnapshotter.git" commit="{Commit}" />
          </metadata>
        </package>
        """).Root!.Elements().Single();

    // Trimmed from nuget.org's catalog entry for GitSnapshotter 0.2.0.
    private static System.Text.Json.JsonDocument CatalogEntry(string? replace = null, string? with = null)
    {
        var json = $$"""
            {
              "id": "GitSnapshotter",
              "version": "0.2.0",
              "authors": "BusHero",
              "description": "Take a snapshot.",
              "iconFile": "icon.png",
              "licenseExpression": "MIT",
              "listed": true,
              "readmeFile": "README.md",
              "repository": { "branch": "refs/heads/trunk", "commit": "{{Commit}}", "type": "git", "url": "https://github.com/BusHero/GitSnapshotter.git" }
            }
            """;
        return System.Text.Json.JsonDocument.Parse(
            replace is { Length: > 0 } ? json.Replace(replace, with, StringComparison.Ordinal) : json);
    }

    [Test]
    public async Task FeedShowingThePackagesMetadataPasses()
    {
        using var catalog = CatalogEntry();

        await Assert.That(SmokeTestChecks.CompareMetadata(PackageMetadata, catalog.RootElement, Commit)).IsEmpty();
    }

    [Test]
    [Arguments("\"listed\": true", "\"listed\": false", "the version is not listed")]
    [Arguments("\"licenseExpression\": \"MIT\",", "", "the license is ''")]
    [Arguments("\"iconFile\": \"icon.png\"", "\"iconFile\": \"other.png\"", "the icon is 'other.png'")]
    [Arguments("\"readmeFile\": \"README.md\",", "", "the readme is ''")]
    [Arguments("\"commit\": \"8bdb", "\"commit\": \"0000", "the commit is '0000")]
    public async Task FeedShowingOtherMetadataFails(string replace, string with, string problem)
    {
        using var catalog = CatalogEntry(replace, with);

        await Assert.That(SmokeTestChecks.CompareMetadata(PackageMetadata, catalog.RootElement, Commit))
            .Contains(p => p.StartsWith(problem, StringComparison.Ordinal));
    }

    [Test]
    public async Task PackageBuiltFromAnotherCommitFails()
    {
        using var catalog = CatalogEntry();

        await Assert.That(SmokeTestChecks.CompareMetadata(PackageMetadata, catalog.RootElement, "abc123"))
            .IsEquivalentTo([$"the package was built from {Commit}, not abc123"]);
    }

    [Test]
    public async Task ServiceIndexResourceIsTheFirstTypeFound()
    {
        using var index = System.Text.Json.JsonDocument.Parse("""
            { "resources": [
                { "@id": "https://example.test/registration-semver1/", "@type": "RegistrationsBaseUrl" },
                { "@id": "https://example.test/registration-semver2/", "@type": "RegistrationsBaseUrl/3.6.0" }
            ] }
            """);

        await Assert.That(SmokeTestChecks.ServiceResource(index.RootElement, "RegistrationsBaseUrl/3.6.0", "RegistrationsBaseUrl"))
            .IsEqualTo("https://example.test/registration-semver2/");
        await Assert.That(() => SmokeTestChecks.ServiceResource(index.RootElement, "ReadmeUriTemplate/6.13.0"))
            .Throws<InvalidOperationException>();
    }

    // ---- Symbols --------------------------------------------------------------------------------------------------

    // This test assembly is built like the packages' assemblies: deterministic, with a portable PDB and its checksum.
    private static string ThisAssembly => typeof(SmokeTestChecksTests).Assembly.Location;

    private static SmokeTestChecks.SymbolQuery ThisQuery() =>
        SmokeTestChecks.SymbolQueryFor(File.ReadAllBytes(ThisAssembly), "test assembly");

    [Test]
    public async Task SymbolQueryIsTheSsqpKeyAndChecksumOfThePdb()
    {
        var query = ThisQuery();

        using (Assert.Multiple())
        {
            await Assert.That(query.PdbName).IsEqualTo("GitSnapshotter.Pipeline.Tests.pdb");
            await Assert.That(query.Key)
                .IsEqualTo($"gitsnapshotter.pipeline.tests.pdb/{query.Guid:N}ffffffff/gitsnapshotter.pipeline.tests.pdb");
            await Assert.That(query.Checksum).Matches("^SHA256:[0-9a-f]{64}$");
        }
    }

    [Test]
    public async Task TheAssemblysOwnPdbPasses()
    {
        var pdb = await File.ReadAllBytesAsync(Path.ChangeExtension(ThisAssembly, ".pdb"));

        await Assert.That(() => SmokeTestChecks.CheckPdb(pdb, ThisQuery())).ThrowsNothing();
    }

    [Test]
    public async Task AnotherAssemblysPdbFails()
    {
        // The tests compile the pipeline's sources into this assembly; Constants is a project of its own.
        var other = Path.Combine(Path.GetDirectoryName(ThisAssembly)!, "Constants.pdb");
        var pdb = await File.ReadAllBytesAsync(other);

        await Assert.That(() => SmokeTestChecks.CheckPdb(pdb, ThisQuery()))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("has ID", StringComparison.Ordinal);
    }

    [Test]
    public async Task ChangedPdbFails()
    {
        var pdb = await File.ReadAllBytesAsync(Path.ChangeExtension(ThisAssembly, ".pdb"));
        pdb[^1] ^= 0xFF;

        await Assert.That(() => SmokeTestChecks.CheckPdb(pdb, ThisQuery()))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("hashes to", StringComparison.Ordinal);
    }

    [Test]
    public async Task NotAPdbFails()
    {
        await Assert.That(() => SmokeTestChecks.CheckPdb([1, 2, 3], ThisQuery()))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("is not a portable PDB", StringComparison.Ordinal);
    }

    [Test]
    public async Task OwnAssembliesAreTheOnesWithSymbols()
    {
        await Assert.That(SmokeTestChecks.OwnAssemblies(
        [
            "lib/net8.0/GitSnapshotter.dll", "lib/net8.0/GitSnapshotter.xml",
            "tools/net11.0/any/GitSnapshotter.Console.dll", "tools/net11.0/any/LibGit2Sharp.dll", "GitSnapshotter.nuspec",
        ])).IsEquivalentTo(["lib/net8.0/GitSnapshotter.dll", "tools/net11.0/any/GitSnapshotter.Console.dll"]);
    }

    // ---- The release ----------------------------------------------------------------------------------------------

    [Test]
    public async Task ReleaseAssetsMatchingTheirChecksumsPass()
    {
        var release = Path.Combine(_restore.Root, "release");
        Directory.CreateDirectory(release);
        await File.WriteAllTextAsync(Path.Combine(release, "a.nupkg"), "a");
        await File.WriteAllTextAsync(
            Path.Combine(release, "SHA256SUMS.txt"),
            "ca978112ca1bbdcafac231b39a23dc4da786eff8147c4e72b9807785afee48bb  a.nupkg\n");

        await Assert.That(() => SmokeTestChecks.VerifyChecksums(release, "SHA256SUMS.txt")).ThrowsNothing();

        await File.WriteAllTextAsync(Path.Combine(release, "a.nupkg"), "b");
        await Assert.That(() => SmokeTestChecks.VerifyChecksums(release, "SHA256SUMS.txt"))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("a.nupkg has another checksum", StringComparison.Ordinal);
    }

    // ---- Snapshot and restore ---------------------------------------------------------------------------------------

    [Test]
    public async Task SnapshotsAreComparedAsJson()
    {
        using (Assert.Multiple())
        {
            await Assert.That(SmokeTestChecks.SameSnapshot("""{ "Head": "main", "Tags": [] }""", """{"Head":"main","Tags":[]}""")).IsTrue();
            await Assert.That(SmokeTestChecks.SameSnapshot("""{ "Head": "main" }""", """{ "Head": "extra" }""")).IsFalse();
        }
    }

    // ---- Upgrading --------------------------------------------------------------------------------------------------

    [Test]
    [Arguments("0.3.0", "0.2.0")]
    [Arguments("0.2.0", "0.1.0")]
    [Arguments("0.3.0-rc.1", "0.2.0")]
    [Arguments("0.10.0", "0.9.0")]
    public async Task UpgradeIsFromTheNewestStableVersionBefore(string version, string previous)
    {
        string[] published = ["0.1.0", "0.2.0-rc.1", "0.2.0-rc.2", "0.2.0", "0.9.0", "0.10.0", "0.3.0-rc.1", "0.3.0"];

        await Assert.That(SmokeTestChecks.PreviousStable(published, version)).IsEqualTo(previous);
    }

    [Test]
    public async Task FirstVersionHasNothingToUpgradeFrom()
    {
        await Assert.That(SmokeTestChecks.PreviousStable(["0.1.0", "0.2.0"], "0.1.0")).IsNull();
    }

    [Test]
    [Arguments("1.0.0-alpha", "1.0.0-alpha.1")]
    [Arguments("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [Arguments("1.0.0-beta.2", "1.0.0-beta.11")]
    [Arguments("1.0.0-rc.1", "1.0.0")]
    [Arguments("1.0.0", "1.0.1")]
    [Arguments("1.9.0", "1.10.0")]
    public async Task VersionsAreOrderedAsSemVer(string lower, string higher)
    {
        using (Assert.Multiple())
        {
            await Assert.That(SmokeTestChecks.CompareVersions(lower, higher)).IsLessThan(0);
            await Assert.That(SmokeTestChecks.CompareVersions(higher, lower)).IsGreaterThan(0);
            await Assert.That(SmokeTestChecks.CompareVersions(higher, higher)).IsEqualTo(0);
        }
    }

    // ---- Known vulnerabilities --------------------------------------------------------------------------------------

    [Test]
    public async Task AuditProjectFailsOnAnyKnownVulnerability()
    {
        var project = XDocument.Parse(SmokeTestChecks.AuditProject(ToolNoticed, "net11.0")).Root!;
        string Property(string name) => project.Descendants(name).Single().Value;

        using (Assert.Multiple())
        {
            await Assert.That(Property("NuGetAudit")).IsEqualTo("true");
            await Assert.That(Property("NuGetAuditMode")).IsEqualTo("all");
            await Assert.That(Property("NuGetAuditLevel")).IsEqualTo("low");
            await Assert.That(Property("WarningsAsErrors").Split(';')).IsEquivalentTo(["NU1900", "NU1901", "NU1902", "NU1903", "NU1904"]);
            await Assert.That(project.Descendants("PackageReference").Select(r => $"{r.Attribute("Include")!.Value} {r.Attribute("Version")!.Value}"))
                .IsEquivalentTo(["LibGit2Sharp [0.32.0]", "LibGit2Sharp.NativeBinaries [2.0.323]", "System.CommandLine [2.0.14]"]);
        }
    }

    // ---- Deterministic builds and Source Link -------------------------------------------------------------------------

    [Test]
    public async Task ThisAssemblyIsDeterministic()
    {
        // Deterministic is the SDK's default; the build-machine path rule depends on where the test runs.
        var assembly = await File.ReadAllBytesAsync(ThisAssembly);

        await Assert.That(SmokeTestChecks.CheckDeterministic(assembly, "test assembly"))
            .DoesNotContain(problem => problem.Contains("deterministically", StringComparison.Ordinal));
    }

    private const string Raw = "https://raw.githubusercontent.com/BusHero/GitSnapshotter/5be46049fd0c92b3fae46587a7374e43d68e8a81/";

    private static readonly (string, string)[] SourceLinkMap = [("/_/*", Raw + "*"), ("/_/docs/notes.md", "https://example.test/notes.md")];

    [Test]
    [Arguments("/_/Source/GitSnapshotter/GitRepository.cs", Raw + "Source/GitSnapshotter/GitRepository.cs")]
    [Arguments("/_/docs/notes.md", "https://example.test/notes.md")]
    public async Task SourcePathsAreMappedToTheirUrl(string path, string expected)
    {
        ArgumentNullException.ThrowIfNull(path);

        await Assert.That(SmokeTestChecks.MapSource(SourceLinkMap, path)).IsEqualTo(expected);
    }

    [Test]
    public async Task SourceOutsideTheMapIsNotMapped()
    {
        await Assert.That(SmokeTestChecks.MapSource(SourceLinkMap, "/home/runner/.nuget/packages/polyfill/Polyfill.cs")).IsNull();
    }

    [Test]
    public async Task SourceLinkMapIsRead()
    {
        var json = System.Text.Encoding.UTF8.GetBytes($$$"""{"documents":{"/_/*":"{{{Raw}}}*"}}""");

        await Assert.That(SmokeTestChecks.ReadSourceLinkMap(json)).IsEquivalentTo([("/_/*", Raw + "*")]);
    }

    [Test]
    public async Task DownloadedSourceIsCheckedAgainstThePdbsHash()
    {
        byte[] content = [1, 2, 3];
        var file = new SmokeTestChecks.SourceFile("/_/a.cs", Raw + "a.cs", System.Security.Cryptography.SHA256.HashData(content));

        using (Assert.Multiple())
        {
            await Assert.That(SmokeTestChecks.SourceMatches(content, file)).IsTrue();
            await Assert.That(SmokeTestChecks.SourceMatches([1, 2, 4], file)).IsFalse();
        }
    }

    // ---- The GitHub release -----------------------------------------------------------------------------------------

    private static readonly ReleaseVersion Stable = ReleaseVersion.Parse("0.3.0");

    private static SmokeTestChecks.GitHubRelease PublishedRelease(string version = "0.3.0", string latest = "v0.3.0") =>
        new(false, version.Contains('-', StringComparison.Ordinal), "Notes\r\n", SmokeTestChecks.ReleaseAssets(version), Commit, latest);

    [Test]
    public async Task PublishedReleasePasses()
    {
        await Assert.That(SmokeTestChecks.CheckGitHubRelease(PublishedRelease(), Stable, Commit, "Notes")).IsEmpty();
    }

    [Test]
    public async Task ReleaseReplacedByANewerOnePasses()
    {
        await Assert.That(SmokeTestChecks.CheckGitHubRelease(PublishedRelease(latest: "v0.4.0"), Stable, Commit, "Notes")).IsEmpty();
    }

    [Test]
    public async Task PrereleaseThatIsNotLatestPasses()
    {
        var rc = ReleaseVersion.Parse("0.4.0-rc.1");

        await Assert.That(SmokeTestChecks.CheckGitHubRelease(PublishedRelease("0.4.0-rc.1"), rc, Commit, "Notes")).IsEmpty();
    }

    [Test]
    public async Task WrongReleaseReportsEveryProblem()
    {
        var release = PublishedRelease() with
        {
            Draft = true,
            Prerelease = true,
            Body = "Other notes",
            Assets = [.. SmokeTestChecks.ReleaseAssets("0.3.0").Skip(1), "extra.txt"],
            TagCommit = "abc123",
            LatestTag = "v0.2.0",
        };

        await Assert.That(SmokeTestChecks.CheckGitHubRelease(release, Stable, Commit, "Notes")).IsEquivalentTo(
        [
            "it is still a draft",
            "it is marked as a prerelease",
            "it is not the latest release; the older v0.2.0 is",
            $"v0.3.0 points at abc123, not {Commit}",
            "its description is not the version's CHANGELOG.md section",
            "GitSnapshotter.0.3.0.nupkg is not attached",
            "extra.txt is attached, but not expected",
        ]);
    }

    [Test]
    public async Task PrereleaseMarkedLatestFails()
    {
        var rc = ReleaseVersion.Parse("0.4.0-rc.1");

        await Assert.That(SmokeTestChecks.CheckGitHubRelease(PublishedRelease("0.4.0-rc.1", "v0.4.0-rc.1"), rc, Commit, "Notes"))
            .IsEquivalentTo(["it is marked as the latest release, though it is a prerelease"]);
    }

    [Test]
    public async Task ToolFrameworksAreTheConsoleAppBuildsInThePackage()
    {
        await Assert.That(SmokeTestChecks.ToolFrameworks(
        [
            "tools/net8.0/any/GitSnapshotter.Console.dll", "tools/net8.0/any/GitSnapshotter.dll",
            "tools/net11.0/any/GitSnapshotter.Console.dll", "tools/net11.0/any/GitSnapshotter.Console.pdb",
            "lib/net8.0/GitSnapshotter.Console.dll",
        ])).IsEquivalentTo(["net11.0", "net8.0"], CollectionOrdering.Matching);
    }

    // ---- Feeds ----------------------------------------------------------------------------------------------------

    [Test]
    public async Task OurPackagesComeFromTheTestedFeedAndTheRestFromNuGetOrg()
    {
        var configuration = XDocument.Parse(SmokeTestChecks.NuGetConfig(
            "https://nuget.pkg.github.com/BusHero/index.json", SmokeTestChecks.NuGetOrg, "someone", "token")).Root!;
        string[] Patterns(string key) =>
        [
            .. configuration.Element("packageSourceMapping")!.Elements()
                .Single(source => source.Attribute("key")!.Value == key)
                .Elements().Select(package => package.Attribute("pattern")!.Value),
        ];

        using (Assert.Multiple())
        {
            await Assert.That(configuration.Element("packageSources")!.Elements("add").Select(add => add.Attribute("value")!.Value))
                .IsEquivalentTo(["https://nuget.pkg.github.com/BusHero/index.json", SmokeTestChecks.NuGetOrg]);
            await Assert.That(Patterns("smoke-test")).IsEquivalentTo([SmokeTestChecks.LibraryId, SmokeTestChecks.ToolId]);
            await Assert.That(Patterns("dependencies")).IsEquivalentTo(["*"]);
            await Assert.That(configuration.Element("packageSourceCredentials")!.Element("smoke-test")!.Elements()
                    .Select(add => $"{add.Attribute("key")!.Value}={add.Attribute("value")!.Value}"))
                .IsEquivalentTo(["Username=someone", "ClearTextPassword=token"]);
        }
    }

    [Test]
    public async Task NuGetOrgAloneNeedsNoMappingOrCredentials()
    {
        var configuration = XDocument.Parse(SmokeTestChecks.NuGetConfig(SmokeTestChecks.NuGetOrg, SmokeTestChecks.NuGetOrg)).Root!;

        using (Assert.Multiple())
        {
            await Assert.That(configuration.Element("packageSources")!.Elements("add").Count()).IsEqualTo(1);
            await Assert.That(configuration.Element("packageSourceMapping")).IsNull();
            await Assert.That(configuration.Element("packageSourceCredentials")).IsNull();
        }
    }

    [Test]
    [Arguments(new[] { "GitSnapshotter.0.4.0-trunk-20261114.nupkg", "GitSnapshotter.Tool.0.4.0-trunk-20261114.nupkg", "GitSnapshotter.0.3.0-old.nupkg" }, "trunk-20261114", "0.4.0-trunk-20261114")]
    [Arguments(new[] { "GitSnapshotter.Tool.0.4.0-x.nupkg" }, "x", null)]
    public async Task PackedVersionIsTheLibrarysWithTheSuffix(string[] files, string suffix, string? expected)
    {
        await Assert.That(PackageVersion.Packed(files, suffix)).IsEqualTo(expected);
    }

    // ---- Breaking changes and size ----------------------------------------------------------------------------------

    [Test]
    [Arguments("### Breaking changes\n\n- `Restore` throws when...", true)]
    [Arguments("### Changed (breaking)\n", true)]
    [Arguments("### Changed\n\n- Nothing breaking here.", false)]
    [Arguments("", false)]
    public async Task OnlyAChangelogHeadingAllowsBreakingChanges(string notes, bool allowed)
    {
        await Assert.That(SmokeTestChecks.AllowsBreakingChanges(notes)).IsEqualTo(allowed);
    }

    private static Dictionary<string, byte[]> Sized(params (string Name, int Size)[] entries) =>
        entries.ToDictionary(e => e.Name, e => new byte[e.Size]);

    [Test]
    public async Task PackageGrowingWithinTheLimitPasses()
    {
        var previous = Sized(("tools/net11.0/any/a.dll", 1_000_000), ("README.md", 1000));
        var current = Sized(("tools/net11.0/any/a.dll", 1_200_000), ("README.md", 2000), (".signature.p7s", 50_000));

        await Assert.That(SmokeTestChecks.CheckGrowth(current, previous, 25)).IsEmpty();
    }

    [Test]
    public async Task PartGrowingTooMuchFails()
    {
        var previous = Sized(("lib/net8.0/GitSnapshotter.dll", 1_000_000));
        var current = Sized(("lib/net8.0/GitSnapshotter.dll", 1_300_000));

        await Assert.That(SmokeTestChecks.CheckGrowth(current, previous, 25)).IsEquivalentTo(
            [$"lib/net8.0 is {1_300_000:N0} bytes, 30% more than the previous release's {1_000_000:N0}"]);
    }

    [Test]
    public async Task NewFrameworkIsComparedWithThePreviousLargestOfItsKind()
    {
        // 0.4.0 adds a net8.0 build of the tool next to net11.0: each is compared with 0.3.0's net11.0 build.
        var previous = Sized(("tools/net11.0/any/a.dll", 1_000_000));
        var current = Sized(("tools/net8.0/any/a.dll", 1_000_000), ("tools/net11.0/any/a.dll", 1_000_000));
        var bloated = Sized(("tools/net8.0/any/a.dll", 2_000_000), ("tools/net11.0/any/a.dll", 1_000_000));

        using (Assert.Multiple())
        {
            await Assert.That(SmokeTestChecks.CheckGrowth(current, previous, 25)).IsEmpty();
            await Assert.That(SmokeTestChecks.CheckGrowth(bloated, previous, 25)).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task SmallPartsMayGrowBySomeKilobytes()
    {
        await Assert.That(SmokeTestChecks.CheckGrowth(Sized(("README.md", 30_000)), Sized(("README.md", 1000)), 25)).IsEmpty();
    }

    // ---- Local tools ------------------------------------------------------------------------------------------------

    [Test]
    public async Task ToolManifestPinsTheVersion()
    {
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(SmokeTestChecks.ToolManifest("0.4.0-rc.1"))!;
        var tool = manifest["tools"]!["gitsnapshotter.tool"]!;

        using (Assert.Multiple())
        {
            await Assert.That(manifest["isRoot"]!.GetValue<bool>()).IsTrue();
            await Assert.That(tool["version"]!.GetValue<string>()).IsEqualTo("0.4.0-rc.1");
            await Assert.That(tool["commands"]![0]!.GetValue<string>()).IsEqualTo("gitsnapshotter");
        }
    }

    // ---- The docs -----------------------------------------------------------------------------------------------------

    // As Starlight's code blocks render it: a token per span, entities, a copy button carrying the code again.
    private const string InstallationPage = """
        <html><head><style>.x{}</style><script>var a = "dotnet tool install --global Wrong";</script></head><body>
        <p>The <code>GitSnapshotter</code> package targets <code>net8.0</code> and <code>net472</code>.</p>
        <pre><code><span>dotnet</span><span> add package </span><span>GitSnapshotter</span></code></pre>
        <p>It needs the <a href="https://dotnet.microsoft.com/download">.NET 8 runtime</a> or later.</p>
        <pre><code><span>dotnet tool install</span> <span>--global</span> <span>GitSnapshotter.Tool</span></code></pre>
        <button data-code="dotnet tool install --global GitSnapshotter.Tool">Copy</button>
        <p>Use &lt;PackageReference&gt; &amp; friends.</p>
        </body></html>
        """;

    [Test]
    public async Task PageTextDropsTagsAndScriptsAndDecodesEntities()
    {
        var text = SmokeTestChecks.PageText(InstallationPage);

        using (Assert.Multiple())
        {
            await Assert.That(text).Contains("dotnet tool install --global GitSnapshotter.Tool");
            await Assert.That(text).Contains("Use <PackageReference> & friends.");
            await Assert.That(text).DoesNotContain("Wrong");
            await Assert.That(text).DoesNotContain("<span>");
        }
    }

    [Test]
    public async Task InstallationPageMatchingTheReleasePasses()
    {
        var text = SmokeTestChecks.PageText(InstallationPage);

        await Assert.That(SmokeTestChecks.CheckDocs(text, ["net8.0", "net472"], ["net8.0", "net11.0"])).IsEmpty();
    }

    [Test]
    public async Task InstallationPageBehindTheReleaseFails()
    {
        var text = SmokeTestChecks.PageText(InstallationPage);

        // A release whose tool needs .NET 11 and whose library adds netstandard2.0, on a page that says neither.
        await Assert.That(SmokeTestChecks.CheckDocs(text, ["net8.0", "net472", "netstandard2.0"], ["net11.0"])).IsEquivalentTo(
        [
            "it doesn't say the library is built for netstandard2.0",
            "it doesn't say the tool needs the .NET 11 runtime",
        ]);
    }

    [Test]
    public async Task InstallationPageWithoutTheCommandsFails()
    {
        await Assert.That(SmokeTestChecks.CheckDocs("Nothing to see.", [], [])).IsEquivalentTo(
        [
            "it doesn't give 'dotnet add package GitSnapshotter'",
            "it doesn't give 'dotnet tool install --global GitSnapshotter.Tool'",
        ]);
    }

    [Test]
    [Arguments("0.3.0-trunk-20261114", "0.3.0")]
    [Arguments("0.2.5", "0.3.0")]
    [Arguments("0.3.0", "0.3.0")]
    [Arguments("0.4.0-trunk-20261114", null)]
    [Arguments("0.4.0-rc.1", null)]
    public async Task BuildSortingBelowAReleaseIsFound(string version, string? released)
    {
        await Assert.That(SmokeTestChecks.ReleasedAtOrAbove(["0.1.0", "0.2.0", "0.3.0", "0.4.0-rc.1"], version)).IsEqualTo(released);
    }

    [Test]
    public async Task ChecksumsFileIsTheOneTheReleaseAssetsHave()
    {
        await Assert.That(SmokeTestChecks.ChecksumsFile).IsEqualTo(ReleaseAssetsModule.ChecksumsFile);
    }
}
