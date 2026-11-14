using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace GitSnapshotter.Pipeline;

/// <summary>
/// Builds a THIRD-PARTY-NOTICES file from a project's restore graph (<c>obj/project.assets.json</c>): every NuGet
/// package that puts files into the project's output, with its license text from the NuGet packages folder.
/// </summary>
internal static class ThirdPartyNotices
{
    public const string FileName = "THIRD-PARTY-NOTICES.txt";

    /// <summary>What the console app's notices (also in the tool package) call the product.</summary>
    public const string ConsoleProduct = "GitSnapshotter.Console";

    /// <summary>What the library package's notices call the product.</summary>
    public const string LibraryProduct = "The GitSnapshotter package";

    /// <summary>How each package is listed at the top of the file, before its ID and version.</summary>
    public const string ListItem = "  - ";

    /// <summary>Assets that end up in a published app: assemblies, native libraries and compiled-in sources.</summary>
    public static readonly string[] AppAssets = ["runtime", "native", "runtimeTargets", "resource", "contentFiles"];

    /// <summary>
    /// Assets that end up inside a NuGet package's own assemblies: compiled-in sources. Other packages are
    /// dependencies of the package, which consumers restore themselves, so they are not redistributed.
    /// </summary>
    public static readonly string[] PackageAssets = ["contentFiles"];

    /// <summary>
    /// Copyright holders for packages whose nuspec doesn't state one, taken from the project's own license file.
    /// Used only when the package has no copyright of its own.
    /// </summary>
    private static readonly Dictionary<string, string> Copyrights = new(StringComparer.OrdinalIgnoreCase)
    {
        // https://github.com/SimonCropp/Polyfill/blob/main/license.txt
        ["Polyfill"] = "Copyright (c) Simon Cropp",

        // libgit2/libgit2.license.txt in the package (libgit2's COPYING): "libgit2 is Copyright (C) the libgit2 contributors".
        ["LibGit2Sharp.NativeBinaries"] = "Copyright (C) the libgit2 contributors",
    };

    /// <summary>License texts for license expressions whose packages don't include a license file.</summary>
    private static readonly Dictionary<string, string> LicenseTexts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MIT"] = """
            Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
            associated documentation files (the "Software"), to deal in the Software without restriction, including
            without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
            copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the
            following conditions:

            The above copyright notice and this permission notice shall be included in all copies or substantial
            portions of the Software.

            THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
            LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO
            EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN
            AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE
            OR OTHER DEALINGS IN THE SOFTWARE.
            """,
    };

    private sealed record Package(
        string Id,
        string Version,
        string? ProjectUrl,
        string? Copyright,
        string License,
        string LicenseText,
        IReadOnlyList<(string Name, string Text)> Notices);

    /// <summary>Writes the notices for <paramref name="product"/> to <paramref name="outputFile"/>.</summary>
    /// <param name="product">The name the header gives the shipped product.</param>
    /// <param name="assetsFile">The project's <c>obj/project.assets.json</c>; the project must be restored.</param>
    /// <param name="shippedAssets">The asset kinds that count as shipped, e.g. <see cref="AppAssets"/>.</param>
    /// <param name="outputFile">Where to write the notices.</param>
    /// <param name="cancellationToken">Cancels reading the restore graph and writing the file.</param>
    /// <returns>The IDs and versions of the packages listed.</returns>
    public static async Task<IReadOnlyList<string>> WriteAsync(
        string product,
        string assetsFile,
        IReadOnlyCollection<string> shippedAssets,
        string outputFile,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(assetsFile))
        {
            throw new FileNotFoundException($"{assetsFile} not found. Restore the project first.", assetsFile);
        }

        var packages = Collect(assetsFile, shippedAssets);

        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
        await File.WriteAllTextAsync(outputFile, Render(product, packages), new UTF8Encoding(false), cancellationToken);

        return [.. packages.Select(p => $"{p.Id} {p.Version}")];
    }

    private static List<Package> Collect(string assetsFile, IReadOnlyCollection<string> shippedAssets)
    {
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsFile));
        var root = assets.RootElement;

        var packageFolders = root.GetProperty("packageFolders").EnumerateObject().Select(f => f.Name).ToList();
        var libraries = root.GetProperty("libraries");

        // A package is shipped when any target framework takes a real file (not a "_._" placeholder) from it.
        var shipped = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in root.GetProperty("targets").EnumerateObject())
        {
            foreach (var library in target.Value.EnumerateObject())
            {
                if (library.Value.GetProperty("type").GetString() != "package")
                {
                    continue;
                }

                var shipsFiles = shippedAssets.Any(kind =>
                    library.Value.TryGetProperty(kind, out var files)
                    && files.EnumerateObject().Any(file => Path.GetFileName(file.Name) != "_._"));

                if (shipsFiles)
                {
                    shipped.Add(library.Name);
                }
            }
        }

        return
        [
            .. shipped
                .Select(key => ReadPackage(key, libraries.GetProperty(key), packageFolders))
                .OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static Package ReadPackage(string key, JsonElement library, List<string> packageFolders)
    {
        var relativePath = library.GetProperty("path").GetString()!;
        var directory = packageFolders
            .Select(folder => Path.Combine(folder, relativePath))
            .FirstOrDefault(Directory.Exists)
            ?? throw new DirectoryNotFoundException($"Package {key} is not in any packages folder. Restore first.");

        var nuspecFile = Directory.EnumerateFiles(directory, "*.nuspec").Single();
        var metadata = XDocument.Load(nuspecFile).Root!.Elements().Single(e => e.Name.LocalName == "metadata");
        string? Get(string name) =>
            metadata.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } value
                ? value
                : null;

        var id = Get("id")!;
        var version = Get("version")!;
        var licenseElement = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "license");
        var licenseType = licenseElement?.Attribute("type")?.Value;
        var license = licenseElement?.Value.Trim();

        string licenseName, licenseText;
        if (licenseType == "file" && license is not null)
        {
            licenseName = $"see below (from {license} in the package)";
            licenseText = File.ReadAllText(PackageFile(directory, license, id, version));
        }
        else if (licenseType == "expression" && license is not null && LicenseTexts.TryGetValue(license, out var text))
        {
            licenseName = license;
            licenseText = text;
        }
        else
        {
            throw new InvalidOperationException(
                $"Package {id} {version} has license {(license is null ? "(none)" : $"'{license}' ({licenseType})")}, "
                + $"which {nameof(ThirdPartyNotices)} has no text for. Check the package's license and add its text to "
                + $"{nameof(LicenseTexts)}, or replace the package.");
        }

        // Some packages carry notices for code they include themselves (e.g. System.Text.Json's THIRD-PARTY-NOTICES).
        var notices = Directory.EnumerateFiles(directory)
            .Where(file => Path.GetFileName(file).Replace("-", "", StringComparison.Ordinal).StartsWith("THIRDPARTYNOTICE", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .Select(file => (Path.GetFileName(file), File.ReadAllText(file)))
            .ToList();

        var copyright = Get("copyright") ?? Copyrights.GetValueOrDefault(id);

        return new Package(id, version, Get("projectUrl"), copyright, licenseName, licenseText, notices);
    }

    /// <summary>
    /// Resolves a path the nuspec gives (untrusted package content) inside the package directory, so a package can't
    /// point at a file elsewhere on the build machine and have it published in the notices.
    /// </summary>
    private static string PackageFile(string directory, string relativePath, string id, string version)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relativePath));

        // Ordinal: the packages folder is case-sensitive on Linux, and a path that only matches ignoring case is outside it.
        return path.StartsWith(root, StringComparison.Ordinal)
            ? path
            : throw new InvalidOperationException(
                $"Package {id} {version} names license file '{relativePath}', which is outside the package.");
    }

    /// <summary>The start of the file's first line: what it is for.</summary>
    public static string Heading(string product) => $"{product} uses the third-party software listed below.";

    private static string Render(string product, List<Package> packages)
    {
        const string heavy = "================================================================================";
        const string light = "--------------------------------------------------------------------------------";

        var text = new StringBuilder();
        text.Append(Heading(product)).AppendLine(" Each component is distributed under")
            .AppendLine("its own license, reproduced here, and its authors keep their copyrights.")
            .AppendLine()
            .AppendLine("This file is generated by the build pipeline (pipeline/GitSnapshotter.Pipeline) from the")
            .AppendLine("packages the build resolved.")
            .AppendLine();

        foreach (var package in packages)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{ListItem}{package.Id} {package.Version}");
        }

        foreach (var package in packages)
        {
            text.AppendLine().AppendLine(heavy)
                .AppendLine(CultureInfo.InvariantCulture, $"{package.Id} {package.Version}")
                .AppendLine(heavy);
            if (package.ProjectUrl is not null)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"Project:   {package.ProjectUrl}");
            }

            text.AppendLine(package.Copyright is not null
                ? $"Copyright: {package.Copyright}"
                : "Copyright: not stated by the package; see the project's license");

            text.AppendLine(CultureInfo.InvariantCulture, $"License:   {package.License}")
                .AppendLine()
                .AppendLine(Normalize(package.LicenseText));

            foreach (var (name, notice) in package.Notices)
            {
                text.AppendLine().AppendLine(light)
                    .AppendLine(CultureInfo.InvariantCulture, $"{name} from {package.Id}")
                    .AppendLine(light)
                    .AppendLine()
                    .AppendLine(Normalize(notice));
            }
        }

        return text.ToString().ReplaceLineEndings("\n");
    }

    private static string Normalize(string text) => text.ReplaceLineEndings("\n").Trim('\n', '\r', ' ', '﻿');
}
