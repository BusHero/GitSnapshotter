using System.Text.Json;
using System.Text.Json.Nodes;

namespace GitSnapshotter.Pipeline.Tests;

/// <summary>
/// A restore graph (<c>project.assets.json</c>) and a NuGet packages folder in a temporary directory, with only the
/// parts <see cref="ThirdPartyNotices"/> reads.
/// </summary>
internal sealed class FakeRestore : IDisposable
{
    private readonly JsonObject _targets = [];
    private readonly JsonObject _libraries = [];

    public FakeRestore()
    {
        Directory.CreateDirectory(PackagesFolder);
    }

    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"pipeline-tests-{Guid.NewGuid():N}");

    public string PackagesFolder => Path.Combine(Root, "packages");

    public string AssetsFile => Path.Combine(Root, "obj", "project.assets.json");

    public string OutputFile => Path.Combine(Root, "out", ThirdPartyNotices.FileName);

    /// <summary>Adds a package to the packages folder and the restore graph.</summary>
    /// <param name="id">The package ID.</param>
    /// <param name="version">The package version.</param>
    /// <param name="metadata">The nuspec's metadata elements after id and version, as XML.</param>
    /// <param name="assets">The asset kinds and files the target framework takes from the package.</param>
    /// <returns>The package's directory, to put more files in.</returns>
    public string AddPackage(string id, string version, string metadata, params (string Kind, string File)[] assets)
    {
        var relativePath = $"{id.ToUpperInvariant()}/{version}";
        var directory = Path.Combine(PackagesFolder, relativePath);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, $"{id.ToUpperInvariant()}.nuspec"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{id}</id>
                <version>{version}</version>
                {metadata}
              </metadata>
            </package>
            """);

        _libraries[$"{id}/{version}"] = new JsonObject { ["type"] = "package", ["path"] = relativePath };
        AddToTarget("net11.0", $"{id}/{version}", "package", assets);
        return directory;
    }

    /// <summary>Adds a library to a target framework of the restore graph only.</summary>
    public void AddToTarget(string framework, string key, string type, params (string Kind, string File)[] assets)
    {
        if (_targets[framework] is not JsonObject target)
        {
            _targets[framework] = target = [];
        }

        var library = new JsonObject { ["type"] = type };
        foreach (var group in assets.GroupBy(asset => asset.Kind))
        {
            var files = new JsonObject();
            foreach (var (_, file) in group)
            {
                files[file] = new JsonObject();
            }

            library[group.Key] = files;
        }

        target[key] = library;
    }

    public async Task<IReadOnlyList<string>> WriteNoticesAsync(IReadOnlyCollection<string> shippedAssets)
    {
        var assets = new JsonObject
        {
            ["version"] = 3,
            ["targets"] = _targets.DeepClone(),
            ["libraries"] = _libraries.DeepClone(),
            ["packageFolders"] = new JsonObject { [PackagesFolder + Path.DirectorySeparatorChar] = new JsonObject() },
        };

        Directory.CreateDirectory(Path.GetDirectoryName(AssetsFile)!);
        await File.WriteAllTextAsync(AssetsFile, assets.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        return await ThirdPartyNotices.WriteAsync("Product", AssetsFile, shippedAssets, OutputFile, CancellationToken.None);
    }

    public Task<string> ReadNoticesAsync() => File.ReadAllTextAsync(OutputFile);

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
