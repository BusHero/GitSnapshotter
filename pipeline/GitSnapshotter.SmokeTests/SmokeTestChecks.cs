using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

using GitSnapshotter.Pipeline;

namespace GitSnapshotter.SmokeTests;

/// <summary>
/// The checks the smoke tests make on a published version, kept free of any I/O beyond the files they're given, so
/// the pipeline's unit tests (which compile this file) can run them on files of their own.
/// </summary>
internal static class SmokeTestChecks
{
    public const string LibraryId = "GitSnapshotter";

    public const string ToolId = "GitSnapshotter.Tool";

    public const string ToolCommand = "gitsnapshotter";

    /// <summary>The console app's entry assembly, in the tool package and the release zip.</summary>
    public const string ConsoleAssembly = "GitSnapshotter.Console.dll";

    /// <summary>
    /// The checksums file attached to each release, as the pipeline's ReleaseAssetsModule writes it (a pipeline test
    /// checks the two agree).
    /// </summary>
    public const string ChecksumsFile = "SHA256SUMS.txt";

    /// <summary>The repository signature nuget.org adds to every package it accepts.</summary>
    public const string RepositorySignature = ".signature.p7s";

    /// <summary>Lets an SDK build for .NET Framework on any machine, without a targeting pack installed.</summary>
    private const string ReferenceAssemblies = "Microsoft.NETFramework.ReferenceAssemblies";

    private const string ReferenceAssembliesVersion = "1.0.3";

    /// <summary>Files NuGet itself puts in every package, plus nuget.org's signature.</summary>
    private static readonly string[] PackagingFiles = ["_rels/.rels", "[Content_Types].xml", RepositorySignature];

    private const string PackagingMetadataFolder = "package/services/metadata/core-properties/";

    /// <summary>Files both packages carry at their root: the nuspec aside, what the projects pack.</summary>
    private static readonly string[] RootFiles = ["README.md", "icon.png", ThirdPartyNotices.FileName];

    /// <summary>
    /// Lower case, as NuGet's package URLs and symbol server keys are: they are compared as written, not normalized
    /// to upper case.
    /// </summary>
#pragma warning disable CA1308 // The protocols define their keys in lower case.
    public static string Lower(string value) => value.ToLowerInvariant();
#pragma warning restore CA1308

    // ---- Installing --------------------------------------------------------------------------------------------

    /// <summary>nuget.org's V3 feed, where releases and every package the packages depend on come from.</summary>
    public const string NuGetOrg = "https://api.nuget.org/v3/index.json";

    /// <summary>
    /// A nuget.config that uses <paramref name="source"/> only, so no package comes from another feed. When
    /// <paramref name="dependencies"/> is another feed, our two packages come only from <paramref name="source"/>
    /// (package source mapping) and every other package from <paramref name="dependencies"/>, as a feed like GitHub
    /// Packages doesn't mirror nuget.org.
    /// </summary>
    /// <param name="source">The feed our packages are tested from.</param>
    /// <param name="dependencies">The feed for everything else; none or the same feed for <paramref name="source"/> only.</param>
    /// <param name="username">The user name <paramref name="source"/> needs, if any.</param>
    /// <param name="password">The password or token <paramref name="source"/> needs, if any.</param>
    public static string NuGetConfig(string source, string? dependencies = null, string? username = null, string? password = null)
    {
        const string Tested = "smoke-test";
        const string Other = "dependencies";
        var separate = !string.IsNullOrEmpty(dependencies) && !string.Equals(dependencies, source, StringComparison.OrdinalIgnoreCase);

        var sources = new XElement(
            "packageSources",
            new XElement("clear"),
            new XElement("add", new XAttribute("key", Tested), new XAttribute("value", source)));
        var configuration = new XElement("configuration", sources);

        if (separate)
        {
            sources.Add(new XElement("add", new XAttribute("key", Other), new XAttribute("value", dependencies!)));
            configuration.Add(new XElement(
                "packageSourceMapping",
                new XElement(
                    "packageSource",
                    new XAttribute("key", Tested),
                    new XElement("package", new XAttribute("pattern", LibraryId)),
                    new XElement("package", new XAttribute("pattern", ToolId))),
                new XElement("packageSource", new XAttribute("key", Other), new XElement("package", new XAttribute("pattern", "*")))));
        }

        if (!string.IsNullOrEmpty(password))
        {
            configuration.Add(new XElement(
                "packageSourceCredentials",
                new XElement(
                    Tested,
                    new XElement("add", new XAttribute("key", "Username"), new XAttribute("value", username ?? "smoke-test")),
                    new XElement("add", new XAttribute("key", "ClearTextPassword"), new XAttribute("value", password)))));
        }

        return new XDocument(configuration).ToString();
    }

    /// <summary>
    /// An app that references exactly <paramref name="version"/> of the library and builds for each of
    /// <paramref name="targetFrameworks"/>; on a .NET Framework one, also against the reference assemblies package,
    /// so no targeting pack has to be installed.
    /// </summary>
    public static string LibraryProject(string version, IReadOnlyCollection<string> targetFrameworks)
    {
        var references = new XElement(
            "ItemGroup",
            new XElement(
                "PackageReference", new XAttribute("Include", LibraryId), new XAttribute("Version", $"[{version}]")));
        if (targetFrameworks.Any(IsNetFramework))
        {
            references.Add(new XElement(
                "PackageReference",
                new XAttribute("Include", ReferenceAssemblies),
                new XAttribute("Version", ReferenceAssembliesVersion),
                new XAttribute("PrivateAssets", "all")));
        }

        return new XDocument(
            new XElement(
                "Project",
                new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement(
                    "PropertyGroup",
                    new XElement("OutputType", "Exe"),
                    new XElement("TargetFrameworks", string.Join(';', targetFrameworks)),
                    new XElement("LangVersion", "latest"),
                    new XElement("Nullable", "enable")),
                references))
            .ToString();
    }

    /// <summary>
    /// Uses the library as a test would, on the repository in <c>args[0]</c>: takes a snapshot with its working tree;
    /// inside <c>GitRepository.Preserve</c> adds a branch, a tag and an untracked file through LibGit2Sharp (which
    /// loads the native libgit2); then checks the repository is back as it was, and that the snapshot survives a JSON
    /// round trip and restores. Written for every target framework: no implicit usings, which .NET Framework projects
    /// don't get.
    /// </summary>
    public const string LibraryProgram = """
        using System;
        using System.IO;
        using GitSnapshotter;
        using LibGit2Sharp;

        var path = args[0];
        var options = new SnapshotOptions { IncludeWorkingTree = true };
        var force = new RestoreOptions { Force = true };
        var before = GitRepository.GetSnapshot(path, options);

        using (GitRepository.Preserve(path, options, force))
        {
            using var repository = new LibGit2Sharp.Repository(path);
            repository.CreateBranch("library-smoke-test");
            repository.ApplyTag("library-smoke-test");
            File.WriteAllText(Path.Combine(path, "library-smoke-test.txt"), "untracked\n");
        }

        Check("Preserve put the repository back", before, GitRepository.GetSnapshot(path, options));

        var saved = GitRepositorySnapshot.FromJson(before.ToJson());
        Check("the snapshot survives JSON", before, saved);
        using (var repository = new LibGit2Sharp.Repository(path))
        {
            repository.CreateBranch("library-smoke-test-2");
        }

        GitRepository.Restore(path, saved, force);
        Check("Restore put the repository back", before, GitRepository.GetSnapshot(path, options));

        Console.WriteLine(
            $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}: snapshot, Preserve and Restore "
            + $"work on {path} ({before.Branches.Count} branches, {before.Tags.Count} tags)");
        return 0;

        static void Check(string what, GitRepositorySnapshot expected, GitRepositorySnapshot actual)
        {
            var diff = SnapshotDiff.Compare(expected, actual);
            if (diff.HasChanges)
            {
                throw new InvalidOperationException($"Not true that {what}: {string.Join("; ", diff.Changes)}");
            }
        }
        """;

    /// <summary>
    /// The frameworks to run the library on: each it is built for (the package's <c>lib/</c> folders), and the
    /// newest runtime, <paramref name="currentRuntime"/>, which picks the newest of those. .NET Framework only on
    /// Windows, where it runs.
    /// </summary>
    public static IReadOnlyList<string> TestFrameworks(IEnumerable<string> libFrameworks, string currentRuntime, bool windows) =>
    [
        .. libFrameworks
            .Append(currentRuntime)
            .Where(framework => windows || !IsNetFramework(framework))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>Whether <paramref name="framework"/> is a .NET Framework one, like <c>net472</c>.</summary>
    public static bool IsNetFramework(string? framework) =>
        framework is { Length: > 3 }
        && framework.StartsWith("net", StringComparison.OrdinalIgnoreCase)
        && char.IsAsciiDigit(framework[3])
        && !framework.Contains('.', StringComparison.Ordinal);

    /// <summary>Checks that <c>--version</c> printed <paramref name="version"/>, with or without build metadata.</summary>
    /// <exception cref="InvalidOperationException">The app reported another version.</exception>
    public static void CheckToolVersion(string output, string version)
    {
        var reported = output.Trim();
        if (reported != version && !reported.StartsWith(version + "+", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{ToolCommand} --version printed '{reported}', expected {version}.");
        }
    }

    /// <summary>
    /// Finds the package NuGet downloaded under <paramref name="directory"/> (a packages folder, or a tool path's
    /// .store). NuGet keeps the .nupkg next to the files it extracts, with its name in lower case.
    /// </summary>
    /// <exception cref="FileNotFoundException">The package was not downloaded.</exception>
    public static string FindPackage(string directory, string id, string version)
    {
        var name = $"{id}.{version}.nupkg";
        return Directory.EnumerateFiles(directory, "*.nupkg", SearchOption.AllDirectories)
            .FirstOrDefault(file => string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"{name} was not downloaded to {directory}.", name);
    }

    // ---- The package's files --------------------------------------------------------------------------------------

    /// <summary>The files in a package (or any zip), by path.</summary>
    public static Dictionary<string, byte[]> ReadEntries(string zipFile)
    {
        using var zip = ZipFile.OpenRead(zipFile);
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries.Where(entry => !entry.FullName.EndsWith('/', StringComparison.Ordinal)))
        {
            using var stream = entry.Open();
            using var content = new MemoryStream();
            stream.CopyTo(content);
            entries[entry.FullName] = content.ToArray();
        }

        return entries;
    }

    /// <summary>The package's <c>&lt;metadata&gt;</c>, from the nuspec at its root.</summary>
    public static XElement Nuspec(IReadOnlyDictionary<string, byte[]> package)
    {
        var nuspec = package.Single(entry => !entry.Key.Contains('/', StringComparison.Ordinal)
            && entry.Key.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        using var stream = new MemoryStream(nuspec.Value);
        return XDocument.Load(stream).Root!.Elements().Single(e => e.Name.LocalName == "metadata");
    }

    private static string? Value(XElement metadata, string name) =>
        metadata.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();

    private static string? Attribute(XElement metadata, string element, string attribute) =>
        metadata.Elements().FirstOrDefault(e => e.Name.LocalName == element)?.Attribute(attribute)?.Value;

    /// <summary>The commit the package says it was built from (its nuspec's repository element).</summary>
    public static string? RepositoryCommit(XElement metadata) => Attribute(metadata, "repository", "commit");

    /// <summary>The IDs of the packages the nuspec depends on, in any framework's group.</summary>
    public static IReadOnlySet<string> Dependencies(XElement metadata) =>
        metadata.Descendants()
            .Where(e => e.Name.LocalName == "dependency")
            .Select(e => e.Attribute("id")!.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The frameworks the tool package has a build of the console app for: its <c>tools/&lt;framework&gt;/any/</c> folders.</summary>
    public static IReadOnlyList<string> ToolFrameworks(IEnumerable<string> entries) =>
    [
        .. entries
            .Select(entry => entry.Split('/'))
            .Where(parts => parts is ["tools", _, "any", ConsoleAssembly])
            .Select(parts => parts[1])
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>The frameworks the package has assemblies for: its <c>lib/&lt;framework&gt;/</c> folders.</summary>
    public static IReadOnlySet<string> LibFrameworks(IEnumerable<string> entries) =>
        entries
            .Select(entry => entry.Split('/'))
            .Where(parts => parts.Length == 3 && parts[0] == "lib")
            .Select(parts => parts[1])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Checks that <paramref name="downloaded"/> holds the same files, byte for byte, as <paramref name="released"/>.
    /// The files themselves differ: nuget.org adds its <see cref="RepositorySignature"/>, which is left out.
    /// </summary>
    /// <exception cref="InvalidOperationException">A file is missing, extra or different.</exception>
    public static void ComparePackages(string downloaded, string released)
    {
        if (!File.Exists(released))
        {
            throw new FileNotFoundException($"The release has no {Path.GetFileName(released)}.", released);
        }

        var expected = ReadEntries(released);
        var actual = ReadEntries(downloaded);
        actual.Remove(RepositorySignature);

        var differences = expected.Keys.Union(actual.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(name => (expected.TryGetValue(name, out var e), actual.TryGetValue(name, out var a)) switch
            {
                (true, false) => $"{name} is missing",
                (false, true) => $"{name} is extra",
                _ when !e!.SequenceEqual(a!) => $"{name} differs",
                _ => null,
            })
            .OfType<string>()
            .ToList();

        if (differences.Count > 0)
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(downloaded)} from the feed is not the package in the release: "
                + string.Join("; ", differences));
        }
    }

    /// <summary>
    /// Checks that <paramref name="package"/> has <see cref="ThirdPartyNotices.FileName"/> at its root, written for
    /// <paramref name="product"/> and listing at least one package.
    /// </summary>
    /// <returns>The packages the notices cover: their IDs and versions.</returns>
    /// <exception cref="InvalidOperationException">The notices are missing or don't look like the generated file.</exception>
    public static IReadOnlyDictionary<string, string> CheckNotices(string package, string product)
    {
        var entries = ReadEntries(package);
        return entries.TryGetValue(ThirdPartyNotices.FileName, out var notices)
            ? CheckNotices(Encoding.UTF8.GetString(notices), product, Path.GetFileName(package))
            : throw new InvalidOperationException($"{Path.GetFileName(package)} has no {ThirdPartyNotices.FileName}.");
    }

    /// <inheritdoc cref="CheckNotices(string, string)"/>
    /// <param name="notices">The text of the notices.</param>
    /// <param name="product">The product the notices must be written for.</param>
    /// <param name="where">Where the notices are, for the error message.</param>
    public static IReadOnlyDictionary<string, string> CheckNotices(string notices, string product, string where)
    {
        if (!notices.TrimStart('﻿').StartsWith(ThirdPartyNotices.Heading(product), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{ThirdPartyNotices.FileName} in {where} is not the notices for {product}.");
        }

        var packages = notices.Split('\n')
            .TakeWhile(line => !line.StartsWith('=', StringComparison.Ordinal)) // The list comes before the first package's section.
            .Where(line => line.StartsWith(ThirdPartyNotices.ListItem, StringComparison.Ordinal))
            .Select(line => line[ThirdPartyNotices.ListItem.Length..].Split(' '))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : "", StringComparer.OrdinalIgnoreCase);

        return packages.Count > 0
            ? packages
            : throw new InvalidOperationException($"{ThirdPartyNotices.FileName} in {where} lists no packages.");
    }

    /// <summary>
    /// Checks that the library package holds only what it should: the packaging files, the root files, and
    /// GitSnapshotter.dll with its XML docs for exactly the frameworks <paramref name="project"/> builds; and that it
    /// depends only on packages <paramref name="project"/> references without <c>PrivateAssets="all"</c>.
    /// </summary>
    /// <param name="entries">The package's files.</param>
    /// <param name="project">The library's project file (Source/GitSnapshotter/GitSnapshotter.csproj).</param>
    /// <exception cref="InvalidOperationException">Something is in the package that shouldn't be, or missing.</exception>
    public static void CheckLibraryContents(IReadOnlyDictionary<string, byte[]> entries, XDocument project)
    {
        var frameworks = ProjectFrameworks(project);
        var problems = new List<string>();

        var expected = frameworks
            .SelectMany(framework => new[] { $"lib/{framework}/GitSnapshotter.dll", $"lib/{framework}/GitSnapshotter.xml" })
            .Concat(RootFiles)
            .Append($"{LibraryId}.nuspec")
            .ToHashSet(StringComparer.Ordinal);

        problems.AddRange(entries.Keys
            .Where(entry => !expected.Contains(entry) && !IsPackagingFile(entry))
            .Order(StringComparer.Ordinal)
            .Select(entry => $"{entry} is not expected"));
        problems.AddRange(expected
            .Where(entry => !entries.ContainsKey(entry))
            .Order(StringComparer.Ordinal)
            .Select(entry => $"{entry} is missing"));

        var allowed = PublicPackageReferences(project);
        problems.AddRange(Dependencies(Nuspec(entries))
            .Where(dependency => !allowed.Contains(dependency))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(dependency => $"it depends on {dependency}, which the project doesn't pass on to consumers"));

        Fail(LibraryId, problems);
    }

    /// <summary>
    /// Checks that the tool package holds only what it should: the packaging and root files, and under
    /// <c>tools/&lt;framework&gt;/any/</c> the tool's settings, our own assemblies with their symbols, docs and app
    /// files, native libraries, and assemblies (or their resources) of packages <paramref name="noticed"/> lists. A
    /// .NET tool has no package dependencies.
    /// </summary>
    /// <param name="entries">The package's files.</param>
    /// <param name="noticed">The packages the tool's third-party notices cover.</param>
    /// <exception cref="InvalidOperationException">Something is in the package that shouldn't be.</exception>
    public static void CheckToolContents(IReadOnlyDictionary<string, byte[]> entries, IReadOnlyDictionary<string, string> noticed)
    {
        var problems = entries.Keys
            .Where(entry => !IsPackagingFile(entry)
                && !RootFiles.Contains(entry, StringComparer.Ordinal)
                && entry != $"{ToolId}.nuspec"
                && !IsToolFile(entry, noticed))
            .Order(StringComparer.Ordinal)
            .Select(entry => $"{entry} is not expected")
            .ToList();

        if (!entries.Keys.Any(entry => entry.EndsWith("/any/" + ConsoleAssembly, StringComparison.Ordinal)))
        {
            problems.Add($"{ConsoleAssembly} is missing");
        }

        problems.AddRange(Dependencies(Nuspec(entries)).Order(StringComparer.OrdinalIgnoreCase)
            .Select(dependency => $"it depends on {dependency}; a .NET tool ships its dependencies inside it"));

        Fail(ToolId, problems);
    }

    private static bool IsToolFile(string entry, IReadOnlyDictionary<string, string> noticed)
    {
        var parts = entry.Split('/');
        if (parts.Length < 4 || parts[0] != "tools" || parts[2] != "any")
        {
            return false;
        }

        var path = parts[3..];
        var file = path[^1];
        bool Ours(string name) => name.StartsWith("GitSnapshotter.", StringComparison.Ordinal);
        bool Noticed(string assemblyName) => noticed.ContainsKey(assemblyName);

        return path switch
        {
            ["DotnetToolSettings.xml"] => true,

            // GitSnapshotter.dll, GitSnapshotter.Console.dll and their .pdb, .xml, .deps.json, .runtimeconfig.json.
            [_] when Ours(file) && !file.Contains("Test", StringComparison.OrdinalIgnoreCase) => true,

            // A package's assembly (LibGit2Sharp.dll), or its translated resources (de/System.CommandLine.resources.dll).
            [_] when file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) => Noticed(file[..^".dll".Length]),
            [_, _] when file.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase) =>
                Noticed(file[..^".resources.dll".Length]),

            // Native libraries for each platform (libgit2).
            ["runtimes", _, "native", _] => true,

            _ => false,
        };
    }

    private static bool IsPackagingFile(string entry) =>
        PackagingFiles.Contains(entry, StringComparer.Ordinal)
        || (entry.StartsWith(PackagingMetadataFolder, StringComparison.Ordinal) && entry.EndsWith(".psmdcp", StringComparison.Ordinal));

    /// <summary>The project's <c>TargetFrameworks</c> (or <c>TargetFramework</c>).</summary>
    public static IReadOnlySet<string> ProjectFrameworks(XDocument project) =>
        project.Descendants()
            .Where(e => e.Name.LocalName is "TargetFrameworks" or "TargetFramework")
            .SelectMany(e => e.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The packages the project references and passes on to consumers: not <c>PrivateAssets="all"</c>.</summary>
    public static IReadOnlySet<string> PublicPackageReferences(XDocument project) =>
        project.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Where(e => !string.Equals(
                (e.Attribute("PrivateAssets")?.Value ?? e.Elements().FirstOrDefault(c => c.Name.LocalName == "PrivateAssets")?.Value)?.Trim(),
                "all",
                StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Attribute("Include")!.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static void Fail(string package, List<string> problems)
    {
        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"{package} has unexpected contents: {string.Join("; ", problems)}");
        }
    }

    // ---- What nuget.org shows -------------------------------------------------------------------------------------

    /// <summary>
    /// Compares what the feed shows about the package (its catalog entry, from the registration API) with the
    /// package's own nuspec, and checks the version is listed and, when <paramref name="commit"/> is given, was
    /// built from that commit.
    /// </summary>
    /// <returns>What differs; empty when everything matches.</returns>
    public static IReadOnlyList<string> CompareMetadata(XElement nuspec, JsonElement catalog, string? commit)
    {
        var problems = new List<string>();

        string? Catalog(params string[] path)
        {
            var element = catalog;
            foreach (var name in path)
            {
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
                {
                    return null;
                }
            }

            return element.ValueKind == JsonValueKind.String ? element.GetString()!.Trim() : null;
        }

        void Same(string what, string? expected, string? actual)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                problems.Add($"{what} is '{actual}', the package says '{expected}'");
            }
        }

        if (!catalog.TryGetProperty("listed", out var listed) || listed.ValueKind != JsonValueKind.True)
        {
            problems.Add("the version is not listed");
        }

        Same("the version", Value(nuspec, "version"), Catalog("version"));
        Same("the authors", Value(nuspec, "authors"), Catalog("authors"));
        Same("the description", Value(nuspec, "description"), Catalog("description"));
        Same("the license", Value(nuspec, "license"), Catalog("licenseExpression"));
        Same("the icon", Value(nuspec, "icon"), Catalog("iconFile"));
        Same("the readme", Value(nuspec, "readme"), Catalog("readmeFile"));
        Same("the repository", Attribute(nuspec, "repository", "url"), Catalog("repository", "url"));
        Same("the commit", Attribute(nuspec, "repository", "commit"), Catalog("repository", "commit"));

        foreach (var required in new[] { "license", "icon", "readme", "description" })
        {
            if (string.IsNullOrEmpty(Value(nuspec, required)))
            {
                problems.Add($"the package has no {required}");
            }
        }

        if (commit is not null
            && !string.Equals(Attribute(nuspec, "repository", "commit"), commit, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"the package was built from {Attribute(nuspec, "repository", "commit") ?? "an unknown commit"}, not {commit}");
        }

        return problems;
    }

    /// <summary>A resource's URL from a NuGet V3 service index: the first type in <paramref name="types"/> it has.</summary>
    /// <exception cref="InvalidOperationException">The feed has none of them.</exception>
    public static string ServiceResource(JsonElement serviceIndex, params string[] types)
    {
        var resources = serviceIndex.GetProperty("resources").EnumerateArray().ToList();
        foreach (var type in types)
        {
            var resource = resources.FirstOrDefault(r => r.GetProperty("@type").ValueKind == JsonValueKind.String
                && r.GetProperty("@type").GetString() == type);
            if (resource.ValueKind == JsonValueKind.Object)
            {
                return resource.GetProperty("@id").GetString()!;
            }
        }

        throw new InvalidOperationException($"The feed has none of: {string.Join(", ", types)}.");
    }

    // ---- Symbols --------------------------------------------------------------------------------------------------

    /// <summary>How to ask a symbol server for an assembly's portable PDB.</summary>
    /// <param name="Key">The SSQP key: <c>&lt;pdb&gt;/&lt;guid&gt;ffffffff/&lt;pdb&gt;</c>, in lower case.</param>
    /// <param name="Checksum">
    /// The <c>SymbolChecksum</c> header nuget.org's symbol server requires: <c>SHA256:&lt;hex&gt;</c>, from the
    /// assembly's PDB checksum entry.
    /// </param>
    /// <param name="PdbName">The PDB's file name, as the assembly records it.</param>
    /// <param name="Guid">The PDB's ID, which the PDB records too.</param>
    public sealed record SymbolQuery(string Key, string Checksum, string PdbName, Guid Guid);

    /// <summary>Reads the debug directory of an assembly built with a portable PDB.</summary>
    /// <exception cref="InvalidOperationException">The assembly has no portable PDB entry or checksum.</exception>
    public static SymbolQuery SymbolQueryFor(byte[] assembly, string name)
    {
        using var reader = new PEReader(new MemoryStream(assembly));
        var entries = reader.ReadDebugDirectory();

        var codeView = entries.FirstOrDefault(e => e.Type == DebugDirectoryEntryType.CodeView);
        var checksum = entries.FirstOrDefault(e => e.Type == DebugDirectoryEntryType.PdbChecksum);
        if (codeView.Type != DebugDirectoryEntryType.CodeView || !codeView.IsPortableCodeView
            || checksum.Type != DebugDirectoryEntryType.PdbChecksum)
        {
            throw new InvalidOperationException($"{name} was not built with a portable PDB and its checksum.");
        }

        var data = reader.ReadCodeViewDebugDirectoryData(codeView);
        var sum = reader.ReadPdbChecksumDebugDirectoryData(checksum);
        var pdb = Lower(Path.GetFileName(data.Path.Replace('\\', '/')));

        return new SymbolQuery(
            $"{pdb}/{data.Guid:N}ffffffff/{pdb}",
            $"{sum.AlgorithmName}:{Convert.ToHexStringLower(sum.Checksum.AsSpan())}",
            Path.GetFileName(data.Path.Replace('\\', '/')),
            data.Guid);
    }

    /// <summary>Our own assemblies in a package, the ones with symbols: GitSnapshotter*.dll under lib/ or tools/.</summary>
    public static IEnumerable<string> OwnAssemblies(IEnumerable<string> entries) =>
        entries
            .Where(entry => entry.StartsWith("lib/", StringComparison.Ordinal) || entry.StartsWith("tools/", StringComparison.Ordinal))
            .Where(entry => Path.GetFileName(entry).StartsWith("GitSnapshotter", StringComparison.Ordinal)
                && entry.EndsWith(".dll", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    /// <summary>
    /// Checks that <paramref name="pdb"/>, from a symbol server, is the PDB <paramref name="query"/> asked for: its ID
    /// is the assembly's, and it hashes to the assembly's checksum. A portable PDB's checksum is taken over the file
    /// with its 20-byte ID zeroed.
    /// </summary>
    /// <exception cref="InvalidOperationException">It is another PDB, or not a portable PDB.</exception>
    public static void CheckPdb(byte[] pdb, SymbolQuery query)
    {
        BlobContentId id;
        int idOffset;
        try
        {
            using var provider = MetadataReaderProvider.FromPortablePdbImage(System.Collections.Immutable.ImmutableArray.Create(pdb));
            var header = provider.GetMetadataReader().DebugMetadataHeader
                ?? throw new BadImageFormatException("No #Pdb stream.");
            id = new BlobContentId(header.Id);
            idOffset = header.IdStartOffset;
        }
        catch (BadImageFormatException exception)
        {
            throw new InvalidOperationException($"The symbol server's {query.PdbName} is not a portable PDB: {exception.Message}", exception);
        }

        if (id.Guid != query.Guid)
        {
            throw new InvalidOperationException($"The symbol server's {query.PdbName} has ID {id.Guid:N}, the assembly {query.Guid:N}.");
        }

        var zeroed = (byte[])pdb.Clone();
        Array.Clear(zeroed, idOffset, 20);
        var actual = $"SHA256:{Convert.ToHexStringLower(SHA256.HashData(zeroed))}";
        if (!string.Equals(actual, query.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The symbol server's {query.PdbName} hashes to {actual}, the assembly expects {query.Checksum}.");
        }
    }

    /// <summary>The PDB the release's symbol package has for <paramref name="assemblyEntry"/>, if any.</summary>
    public static byte[]? ReleasedPdb(IReadOnlyDictionary<string, byte[]> symbolPackage, string assemblyEntry) =>
        symbolPackage.GetValueOrDefault(Path.ChangeExtension(assemblyEntry, ".pdb").Replace('\\', '/'));

    // ---- The release ----------------------------------------------------------------------------------------------

    /// <summary>Checks every file SHA256SUMS.txt names in <paramref name="directory"/> against its checksum.</summary>
    /// <exception cref="InvalidOperationException">A file is missing or has another checksum.</exception>
    public static void VerifyChecksums(string directory, string checksumsFile)
    {
        var problems = new List<string>();
        foreach (var line in File.ReadAllLines(Path.Combine(directory, checksumsFile)).Where(l => l.Length > 0))
        {
            var (hash, name) = (line[..64], line[66..]);
            var file = Path.Combine(directory, name);
            if (!File.Exists(file))
            {
                problems.Add($"{name} is missing");
            }
            else if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))), hash, StringComparison.Ordinal))
            {
                problems.Add($"{name} has another checksum");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"The release assets don't match {checksumsFile}: {string.Join("; ", problems)}");
        }
    }

    // ---- Snapshot and restore ---------------------------------------------------------------------------------------

    /// <summary>Whether two snapshots (the tool's JSON) describe the same repository state.</summary>
    public static bool SameSnapshot(string first, string second) =>
        JsonNode.DeepEquals(JsonNode.Parse(first), JsonNode.Parse(second));

    // ---- Upgrading --------------------------------------------------------------------------------------------------

    /// <summary>
    /// The newest stable version in <paramref name="versions"/> (a flat container's version list) before
    /// <paramref name="version"/>: the one most users upgrade from. None when <paramref name="version"/> is the first.
    /// </summary>
    public static string? PreviousStable(IEnumerable<string> versions, string version) =>
        versions
            .Where(v => !v.Contains('-', StringComparison.Ordinal) && CompareVersions(v, version) < 0)
            .OrderBy(v => v, Comparer<string>.Create(CompareVersions))
            .LastOrDefault();

    /// <summary>
    /// The newest stable version in <paramref name="versions"/> that <paramref name="version"/> doesn't sort above:
    /// a build that sorts below what is released, so <c>--prerelease</c> and updates would never pick it. None when
    /// <paramref name="version"/> is newer than every release.
    /// </summary>
    public static string? ReleasedAtOrAbove(IEnumerable<string> versions, string version) =>
        versions
            .Where(v => !v.Contains('-', StringComparison.Ordinal) && CompareVersions(v, version) >= 0)
            .OrderBy(v => v, Comparer<string>.Create(CompareVersions))
            .LastOrDefault();

    /// <summary>Orders SemVer versions, as NuGet does: by number, a prerelease before its release.</summary>
    public static int CompareVersions(string? left, string? right)
    {
        static (int[] Numbers, string[]? Labels) Split(string? version)
        {
            var parts = (version ?? "").Split('+')[0].Split('-', 2);
            var numbers = parts[0].Split('.').Select(n => int.Parse(n, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            return (numbers, parts.Length > 1 ? parts[1].Split('.') : null);
        }

        var (a, aLabels) = Split(left);
        var (b, bLabels) = Split(right);
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var order = (i < a.Length ? a[i] : 0).CompareTo(i < b.Length ? b[i] : 0);
            if (order != 0)
            {
                return order;
            }
        }

        if (aLabels is null || bLabels is null)
        {
            return (aLabels is null ? 1 : 0) - (bLabels is null ? 1 : 0);
        }

        for (var i = 0; i < Math.Min(aLabels.Length, bLabels.Length); i++)
        {
            var aNumeric = int.TryParse(aLabels[i], out var aNumber);
            var bNumeric = int.TryParse(bLabels[i], out var bNumber);
            var order = (aNumeric, bNumeric) switch
            {
                (true, true) => aNumber.CompareTo(bNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(aLabels[i], bLabels[i]),
            };
            if (order != 0)
            {
                return order;
            }
        }

        return aLabels.Length.CompareTo(bLabels.Length);
    }

    // ---- Known vulnerabilities --------------------------------------------------------------------------------------

    /// <summary>NuGet Audit's warnings: one per severity, and NU1900 when it couldn't read the vulnerability data.</summary>
    public const string AuditWarnings = "NU1900;NU1901;NU1902;NU1903;NU1904";

    /// <summary>
    /// A project whose restore fails when any of <paramref name="packages"/> (ID and exact version), or anything they
    /// depend on, has a known vulnerability of any severity, or when the vulnerability data can't be read.
    /// </summary>
    public static string AuditProject(IEnumerable<KeyValuePair<string, string>> packages, string targetFramework) =>
        new XDocument(
            new XElement(
                "Project",
                new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement(
                    "PropertyGroup",
                    new XElement("TargetFramework", targetFramework),
                    new XElement("NuGetAudit", "true"),
                    new XElement("NuGetAuditMode", "all"),
                    new XElement("NuGetAuditLevel", "low"),
                    new XElement("WarningsAsErrors", AuditWarnings)),
                new XElement(
                    "ItemGroup",
                    packages.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => new XElement(
                        "PackageReference", new XAttribute("Include", p.Key), new XAttribute("Version", $"[{p.Value}]"))))))
            .ToString();

    // ---- Deterministic builds and Source Link -----------------------------------------------------------------------

    private static readonly Guid SourceLinkKind = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

    private static readonly Guid EmbeddedSourceKind = new("0E8A571B-6926-466E-B4AD-8AB04611F5FE");

    private static readonly Guid CompilationOptionsKind = new("B5FEEC05-8CD0-4A83-96DA-466284BB4BD8");

    private static readonly Guid Sha256Algorithm = new("8829d00f-11b8-4213-878b-770e8597ac16");

    /// <summary>
    /// Where the build's sources are mapped to, so no build machine path ends up in the assemblies
    /// (<c>ContinuousIntegrationBuild</c>).
    /// </summary>
    public const string MappedSourceRoot = "/_/";

    /// <summary>
    /// Checks that an assembly was built deterministically on a build server: it has a Reproducible debug directory
    /// entry, and its PDB path is under <see cref="MappedSourceRoot"/>.
    /// </summary>
    /// <returns>What is wrong; empty when nothing is.</returns>
    public static IReadOnlyList<string> CheckDeterministic(byte[] assembly, string name)
    {
        using var reader = new PEReader(new MemoryStream(assembly));
        var entries = reader.ReadDebugDirectory();
        var problems = new List<string>();

        if (!entries.Any(e => e.Type == DebugDirectoryEntryType.Reproducible))
        {
            problems.Add($"{name} was not built deterministically");
        }

        var codeView = entries.FirstOrDefault(e => e.Type == DebugDirectoryEntryType.CodeView);
        var path = codeView.Type == DebugDirectoryEntryType.CodeView ? reader.ReadCodeViewDebugDirectoryData(codeView).Path : null;
        if (path is null || !path.StartsWith(MappedSourceRoot, StringComparison.Ordinal))
        {
            problems.Add($"{name} records its PDB as '{path}', a build machine path, not one under {MappedSourceRoot}");
        }

        return problems;
    }

    /// <summary>A source file a PDB names, where Source Link says to download it from, and its hash.</summary>
    public sealed record SourceFile(string Document, string Url, byte[] Sha256);

    /// <summary>
    /// Checks a PDB's Source Link: it has a map; it was compiled with optimizations; every source file it names is
    /// either embedded in it or mapped to a URL at <paramref name="commit"/>, with a SHA-256 hash to check what that
    /// URL returns against.
    /// </summary>
    /// <returns>What is wrong, and the files to download and check.</returns>
    public static (IReadOnlyList<string> Problems, IReadOnlyList<SourceFile> Files) CheckSourceLink(byte[] pdb, string name, string commit)
    {
        using var provider = MetadataReaderProvider.FromPortablePdbImage(System.Collections.Immutable.ImmutableArray.Create(pdb));
        var reader = provider.GetMetadataReader();
        var problems = new List<string>();
        var files = new List<SourceFile>();

        byte[]? Information(EntityHandle parent, Guid kind) =>
            reader.GetCustomDebugInformation(parent)
                .Select(reader.GetCustomDebugInformation)
                .Where(information => reader.GetGuid(information.Kind) == kind)
                .Select(information => reader.GetBlobBytes(information.Value))
                .FirstOrDefault();

        var options = Information(EntityHandle.ModuleDefinition, CompilationOptionsKind) is { } blob
            ? Encoding.UTF8.GetString(blob).Split('\0')
            : [];
        var optimization = Enumerable.Range(0, options.Length / 2)
            .Where(i => options[2 * i] == "optimization")
            .Select(i => options[(2 * i) + 1])
            .FirstOrDefault();
        if (optimization is null || !optimization.StartsWith("release", StringComparison.Ordinal))
        {
            problems.Add($"{name} was compiled with optimization '{optimization}', not as a release build");
        }

        var map = Information(EntityHandle.ModuleDefinition, SourceLinkKind) is { } json
            ? ReadSourceLinkMap(json)
            : null;
        if (map is null)
        {
            problems.Add($"{name} has no Source Link map");
            return (problems, files);
        }

        foreach (var (pattern, url) in map)
        {
            if (!url.Contains(commit, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{name} maps {pattern} to {url}, not to commit {commit}");
            }
        }

        foreach (var handle in reader.Documents)
        {
            var document = reader.GetDocument(handle);
            var path = reader.GetString(document.Name);
            if (Information(handle, EmbeddedSourceKind) is not null)
            {
                continue; // The debugger takes it from the PDB.
            }

            var mapped = MapSource(map, path);
            if (mapped is null)
            {
                problems.Add($"{name} names {path}, which is neither embedded nor mapped to a URL");
            }
            else if (reader.GetGuid(document.HashAlgorithm) != Sha256Algorithm)
            {
                problems.Add($"{name} hashes {path} with another algorithm than SHA-256");
            }
            else
            {
                files.Add(new SourceFile(path, mapped, reader.GetBlobBytes(document.Hash)));
            }
        }

        return (problems, files);
    }

    /// <summary>The <c>documents</c> map of a Source Link JSON file: path patterns to URL patterns.</summary>
    public static IReadOnlyList<(string Pattern, string Url)> ReadSourceLinkMap(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        return
        [
            .. document.RootElement.GetProperty("documents").EnumerateObject()
                .Select(entry => (entry.Name, entry.Value.GetString()!)),
        ];
    }

    /// <summary>
    /// Maps a source path with a Source Link map: an exact path, or a pattern ending in <c>*</c> whose rest replaces
    /// the URL's <c>*</c>, with forward slashes. The longest matching pattern wins.
    /// </summary>
    public static string? MapSource(IReadOnlyList<(string Pattern, string Url)> map, string path)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(path);

        foreach (var (pattern, url) in map.OrderByDescending(entry => entry.Pattern.Length))
        {
            if (pattern.EndsWith('*', StringComparison.Ordinal))
            {
                var prefix = pattern[..^1];
                if (path.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return url.Replace("*", path[prefix.Length..].Replace('\\', '/'), StringComparison.Ordinal);
                }
            }
            else if (path == pattern)
            {
                return url;
            }
        }

        return null;
    }

    /// <summary>Checks that a downloaded source file is the one the PDB was built from.</summary>
    public static bool SourceMatches(byte[] content, SourceFile file) => SHA256.HashData(content).AsSpan().SequenceEqual(file.Sha256);

    // ---- The GitHub release -----------------------------------------------------------------------------------------

    /// <summary>What GitHub shows about a release.</summary>
    /// <param name="Draft">Whether it is still a draft.</param>
    /// <param name="Prerelease">Whether it is marked as a prerelease.</param>
    /// <param name="Body">Its description.</param>
    /// <param name="Assets">The names of the files attached to it.</param>
    /// <param name="TagCommit">The commit its tag points at; none when the tag doesn't exist.</param>
    /// <param name="LatestTag">The tag of the repository's latest release.</param>
    public sealed record GitHubRelease(
        bool Draft, bool Prerelease, string? Body, IReadOnlyCollection<string> Assets, string? TagCommit, string? LatestTag);

    /// <summary>The files the release workflow attaches to the release of <paramref name="version"/>.</summary>
    public static IReadOnlyList<string> ReleaseAssets(string version) =>
    [
        $"{LibraryId}.{version}.nupkg", $"{LibraryId}.{version}.snupkg",
        $"{ToolId}.{version}.nupkg", $"{ToolId}.{version}.snupkg",
        $"GitSnapshotter.Console_{version}.zip", ChecksumsFile,
    ];

    /// <summary>
    /// Checks that the GitHub release of <paramref name="release"/> is published, flagged as what it is (a prerelease,
    /// or the latest release unless a newer one is), tagged on <paramref name="commit"/>, described by <paramref name="notes"/> (its
    /// CHANGELOG.md section) and has exactly the release assets.
    /// </summary>
    /// <returns>What is wrong; empty when nothing is.</returns>
    public static IReadOnlyList<string> CheckGitHubRelease(GitHubRelease actual, ReleaseVersion release, string commit, string notes)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(release);

        var problems = new List<string>();
        if (actual.Draft)
        {
            problems.Add("it is still a draft");
        }

        if (actual.Prerelease != release.IsPrerelease)
        {
            problems.Add(release.IsPrerelease ? "it is not marked as a prerelease" : "it is marked as a prerelease");
        }

        // A stable release becomes the latest one, until a newer one replaces it.
        if (!release.IsPrerelease && actual.LatestTag != release.Tag
            && (actual.LatestTag is null || CompareVersions(ReleaseVersion.Normalize(actual.LatestTag), release.Version) < 0))
        {
            problems.Add($"it is not the latest release; the older {actual.LatestTag ?? "(none)"} is");
        }
        else if (release.IsPrerelease && actual.LatestTag == release.Tag)
        {
            problems.Add("it is marked as the latest release, though it is a prerelease");
        }

        if (!string.Equals(actual.TagCommit, commit, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"{release.Tag} points at {actual.TagCommit ?? "nothing"}, not {commit}");
        }

        static string Normalize(string? text) => (text ?? "").ReplaceLineEndings("\n").Trim();
        if (Normalize(actual.Body) != Normalize(notes))
        {
            problems.Add("its description is not the version's CHANGELOG.md section");
        }

        var expected = ReleaseAssets(release.Version);
        problems.AddRange(expected.Except(actual.Assets, StringComparer.Ordinal).Select(asset => $"{asset} is not attached"));
        problems.AddRange(actual.Assets.Except(expected, StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(asset => $"{asset} is attached, but not expected"));

        return problems;
    }

    // ---- Breaking changes and package size ----------------------------------------------------------------------------

    /// <summary>
    /// Whether a version's CHANGELOG.md section owns up to breaking changes: it has a heading with "breaking" in it,
    /// like <c>### Breaking changes</c>. Then the API compatibility check reports them without failing.
    /// </summary>
    public static bool AllowsBreakingChanges(string? notes) =>
        (notes ?? "").Split('\n').Any(line => line.TrimStart().StartsWith('#', StringComparison.Ordinal)
            && line.Contains("breaking", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Compares the size of each part of a package with the previous release's: each <c>lib/&lt;framework&gt;</c> and
    /// <c>tools/&lt;framework&gt;</c> folder, and the rest. A new framework's folder is compared with the previous
    /// release's largest one of the same kind. A part may grow by <paramref name="maxPercent"/>, or by
    /// <paramref name="slackBytes"/>, whichever is more, so small packages don't fail on a few added lines.
    /// </summary>
    /// <returns>Each part that grew too much.</returns>
    public static IReadOnlyList<string> CheckGrowth(
        IReadOnlyDictionary<string, byte[]> current, IReadOnlyDictionary<string, byte[]> previous, int maxPercent, long slackBytes = 64 * 1024)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);

        static Dictionary<string, long> Parts(IReadOnlyDictionary<string, byte[]> entries) =>
            entries
                .Where(entry => entry.Key != RepositorySignature)
                .GroupBy(entry => entry.Key.Split('/') is [("lib" or "tools") and var kind, var framework, _, ..] ? $"{kind}/{framework}" : "(root)")
                .ToDictionary(group => group.Key, group => group.Sum(entry => (long)entry.Value.Length), StringComparer.Ordinal);

        var now = Parts(current);
        var before = Parts(previous);
        var problems = new List<string>();
        foreach (var (part, size) in now.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var kind = part.Split('/')[0];
            var baseline = before.TryGetValue(part, out var same)
                ? same
                : before.Where(p => p.Key.StartsWith(kind + "/", StringComparison.Ordinal)).Select(p => p.Value).DefaultIfEmpty(0).Max();
            if (baseline == 0)
            {
                continue; // Nothing to compare with.
            }

            var allowed = Math.Max(baseline * (100 + maxPercent) / 100, baseline + slackBytes);
            if (size > allowed)
            {
                problems.Add($"{part} is {size:N0} bytes, {(size - baseline) * 100 / baseline}% more than the previous release's {baseline:N0}");
            }
        }

        return problems;
    }

    // ---- Local tools ------------------------------------------------------------------------------------------------

    /// <summary>A tool manifest (<c>.config/dotnet-tools.json</c>) that pins the tool at <paramref name="version"/>.</summary>
    public static string ToolManifest(string version) =>
        new JsonObject
        {
            ["version"] = 1,
            ["isRoot"] = true,
            ["tools"] = new JsonObject
            {
                [Lower(ToolId)] = new JsonObject
                {
                    ["version"] = version,
                    ["commands"] = new JsonArray(ToolCommand),
                    ["rollForward"] = false,
                },
            },
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    // ---- The docs -----------------------------------------------------------------------------------------------------

    /// <summary>A web page's text: its tags dropped, its entities decoded and its white space collapsed.</summary>
    public static string PageText(string html)
    {
        var withoutScripts = System.Text.RegularExpressions.Regex.Replace(
            html, "<(script|style)[^>]*>.*?</\\1>", " ", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var text = System.Text.RegularExpressions.Regex.Replace(withoutScripts, "<[^>]+>", "");
        return System.Text.RegularExpressions.Regex.Replace(System.Net.WebUtility.HtmlDecode(text), "\\s+", " ");
    }

    /// <summary>
    /// Checks the published installation page against what was released: it gives the install commands, names each
    /// framework the library is built for, and the oldest .NET runtime the tool runs on.
    /// </summary>
    /// <returns>What the page gets wrong or leaves out.</returns>
    public static IReadOnlyList<string> CheckDocs(string pageText, IEnumerable<string> libraryFrameworks, IEnumerable<string> toolFrameworks)
    {
        ArgumentNullException.ThrowIfNull(pageText);

        var problems = new List<string>();
        foreach (var command in new[] { $"dotnet add package {LibraryId}", $"dotnet tool install --global {ToolId}" })
        {
            if (!pageText.Contains(command, StringComparison.Ordinal))
            {
                problems.Add($"it doesn't give '{command}'");
            }
        }

        foreach (var framework in libraryFrameworks.Order(StringComparer.Ordinal))
        {
            if (!pageText.Contains(framework, StringComparison.Ordinal))
            {
                problems.Add($"it doesn't say the library is built for {framework}");
            }
        }

        var oldest = toolFrameworks
            .Where(framework => !IsNetFramework(framework))
            .Select(framework => int.TryParse(framework[3..].Split('.')[0], out var major) ? major : int.MaxValue)
            .DefaultIfEmpty(int.MaxValue)
            .Min();
        if (oldest != int.MaxValue && !pageText.Contains($".NET {oldest} runtime", StringComparison.Ordinal))
        {
            problems.Add($"it doesn't say the tool needs the .NET {oldest} runtime");
        }

        return problems;
    }
}
