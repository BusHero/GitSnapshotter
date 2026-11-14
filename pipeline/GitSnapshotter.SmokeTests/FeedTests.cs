using System.Text.Json;

using TUnit.Core;

using static GitSnapshotter.SmokeTests.SmokeTestChecks;

namespace GitSnapshotter.SmokeTests;

/// <summary>What the feed shows about the packages, their symbols, and their sources.</summary>
[ClassDataSource<PublishedRelease>(Shared = SharedType.PerTestSession)]
public sealed class FeedTests(PublishedRelease release)
{
    [Test]
    [Arguments(ToolId)]
    [Arguments(LibraryId)]
    public async Task FeedShowsThePackagesMetadataReadmeAndIcon(string id)
    {
        Skip.Unless(release.Settings.FeedMetadata, "The feed's metadata is not checked (no registration API)");

        var entries = id == ToolId ? release.ToolEntries : release.LibraryEntries;
        await release.RetryAsync($"read the metadata of {id}", async _ =>
        {
            var problems = await FeedProblemsAsync(id, entries);
            if (problems.Count > 0)
            {
                throw new InvalidOperationException($"{release.Settings.Source} shows {id} {release.Version} wrongly: {string.Join("; ", problems)}");
            }
        });
    }

    [Test]
    public async Task SymbolServerHasThePdbOfEachOfOurAssemblies()
    {
        Skip.Unless(release.Settings.Symbols, "Symbols are not checked");

        foreach (var (name, assembly, released) in Assemblies())
        {
            await release.RetryAsync($"download the symbols of {name}", async _ =>
            {
                var query = SymbolQueryFor(assembly, name);
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{release.Settings.SymbolServer.TrimEnd('/')}/{query.Key}"));
                request.Headers.Add("SymbolChecksum", query.Checksum);
                using var response = await release.Http.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var pdb = await response.Content.ReadAsByteArrayAsync();

                CheckPdb(pdb, query);
                if (released is not null && !released.SequenceEqual(pdb))
                {
                    throw new InvalidOperationException($"The symbol server's {query.PdbName} for {name} is not the release's.");
                }
            });
        }
    }

    [Test]
    public async Task OurAssembliesAreDeterministicAndSourceLinkMapsEverySourceFileToTheCommit()
    {
        var problems = new List<string>();
        var files = new Dictionary<string, SourceFile>(StringComparer.Ordinal);
        foreach (var (name, assembly, pdb) in Assemblies())
        {
            problems.AddRange(CheckDeterministic(assembly, name));
            if (pdb is null)
            {
                Console.WriteLine($"No PDB for {name} to check its Source Link with: not in the package, and no release directory.");
                continue;
            }

            var (sourceProblems, sources) = CheckSourceLink(pdb, name, release.Commit);
            problems.AddRange(sourceProblems);
            foreach (var file in sources)
            {
                files.TryAdd(file.Url, file);
            }
        }

        await Assert.That(problems).IsEmpty();

        // Each file as Source Link downloads it must be the one the assemblies were built from.
        var mismatched = new List<string>();
        foreach (var file in files.Values)
        {
            using var response = await release.Http.GetAsync(new Uri(file.Url));
            if (!response.IsSuccessStatusCode)
            {
                mismatched.Add($"{file.Url} returns {(int)response.StatusCode}");
            }
            else if (!SourceMatches(await response.Content.ReadAsByteArrayAsync(), file))
            {
                mismatched.Add($"{file.Url} is not the {file.Document} the assemblies were built from");
            }
        }

        Console.WriteLine($"Source Link maps {files.Count} source files to {release.Commit}");
        await Assert.That(mismatched).IsEmpty();
    }

    // Each of our assemblies in both packages, with its PDB from the release's symbol package or the package itself
    // (the tool carries its PDBs).
    private List<(string Name, byte[] Assembly, byte[]? Pdb)> Assemblies()
    {
        var assemblies = new List<(string, byte[], byte[]?)>();
        foreach (var (id, entries) in new[] { (ToolId, release.ToolEntries), (LibraryId, release.LibraryEntries) })
        {
            var symbols = release.Release is { } directory ? Path.Combine(directory, $"{id}.{release.Version}.snupkg") : null;
            var symbolEntries = symbols is not null && File.Exists(symbols) ? ReadEntries(symbols) : null;
            foreach (var assembly in OwnAssemblies(entries.Keys))
            {
                var pdb = (symbolEntries is null ? null : ReleasedPdb(symbolEntries, assembly)) ?? ReleasedPdb(entries, assembly);
                assemblies.Add(($"{id} {assembly}", entries[assembly], pdb));
            }
        }

        return assemblies;
    }

    private async Task<List<string>> FeedProblemsAsync(string id, IReadOnlyDictionary<string, byte[]> entries)
    {
        using var serviceIndex = JsonDocument.Parse(await release.Http.GetStringAsync(new Uri(release.Settings.Source)));
        var nuspec = Nuspec(entries);
        var package = $"{Lower(id)}/{Lower(release.Version)}";

        var registrations = ServiceResource(serviceIndex.RootElement, "RegistrationsBaseUrl/3.6.0", "RegistrationsBaseUrl");
        using var leaf = JsonDocument.Parse(await release.Http.GetStringAsync(new Uri($"{registrations.TrimEnd('/')}/{package}.json")));
        using var catalog = JsonDocument.Parse(await release.Http.GetStringAsync(new Uri(leaf.RootElement.GetProperty("catalogEntry").GetString()!)));

        var problems = CompareMetadata(nuspec, catalog.RootElement, release.Settings.Commit).ToList();

        // nuget.org serves the readme and the icon from the package; they must be the package's files.
        var flat = ServiceResource(serviceIndex.RootElement, "PackageBaseAddress/3.0.0").TrimEnd('/');
        foreach (var element in new[] { "readme", "icon" })
        {
            if (nuspec.Elements().FirstOrDefault(e => e.Name.LocalName == element)?.Value.Trim() is not { } file)
            {
                continue; // CompareMetadata reports it.
            }

            var url = $"{flat}/{package}/{element}";
            var served = await release.Http.GetByteArrayAsync(new Uri(url));
            if (!entries.TryGetValue(file, out var packaged) || !packaged.SequenceEqual(served))
            {
                problems.Add($"{url} is not the package's {file}");
            }
        }

        return problems;
    }
}
