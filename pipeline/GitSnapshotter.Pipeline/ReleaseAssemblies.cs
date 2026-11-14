using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace GitSnapshotter.Pipeline;

/// <summary>
/// Checks that the project's own assemblies (<c>GitSnapshotter*.dll</c>) in a release carry the release version, so
/// a 1.2.0 release holds 1.2.0 assemblies. Third-party assemblies keep their own versions and are not checked.
/// </summary>
internal static class ReleaseAssemblies
{
    private const string Prefix = "GitSnapshotter";

    /// <summary>
    /// Checks every package in <paramref name="publish"/>, and the console app published next to them.
    /// The assembly and file versions must be <c>major.minor.patch.0</c>, and the informational version the release
    /// version, optionally followed by <c>+</c> and the commit.
    /// </summary>
    /// <returns>One line per problem; empty when the release is consistent.</returns>
    public static IReadOnlyList<string> Verify(string publish, ReleaseVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        var problems = new List<string>();

        foreach (var package in Directory.EnumerateFiles(publish, "*.nupkg").Order(StringComparer.Ordinal))
        {
            using var zip = ZipFile.OpenRead(package);
            var assemblies = zip.Entries
                .Where(entry => IsOwnAssembly(entry.FullName))
                .OrderBy(entry => entry.FullName, StringComparer.Ordinal)
                .ToList();

            if (assemblies.Count == 0)
            {
                problems.Add($"{Path.GetFileName(package)} contains no {Prefix} assemblies.");
            }

            foreach (var entry in assemblies)
            {
                using var content = new MemoryStream();
                using (var entryStream = entry.Open())
                {
                    entryStream.CopyTo(content);
                }

                Check($"{Path.GetFileName(package)}/{entry.FullName}", content.ToArray(), version, problems);
            }
        }

        var published = Directory.EnumerateFiles(publish, "*.dll")
            .Where(IsOwnAssembly)
            .Order(StringComparer.Ordinal)
            .ToList();

        if (published.Count == 0)
        {
            problems.Add($"The published console app contains no {Prefix} assemblies.");
        }

        foreach (var file in published)
        {
            Check(Path.GetFileName(file), File.ReadAllBytes(file), version, problems);
        }

        return problems;
    }

    private static bool IsOwnAssembly(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith(Prefix, StringComparison.Ordinal)
            && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
    }

    private static void Check(string location, byte[] content, ReleaseVersion version, List<string> problems)
    {
        var numeric = NumericVersion(version);

        using var pe = new PEReader(ImmutableArray.Create(content));
        if (!pe.HasMetadata)
        {
            problems.Add($"{location} is not a .NET assembly.");
            return;
        }

        var reader = pe.GetMetadataReader();
        var assembly = reader.GetAssemblyDefinition();

        if (assembly.Version != numeric)
        {
            problems.Add($"{location} has assembly version {assembly.Version}, expected {numeric}.");
        }

        var fileVersion = StringAttribute(reader, assembly, "AssemblyFileVersionAttribute");
        if (fileVersion != numeric.ToString())
        {
            problems.Add($"{location} has file version {fileVersion ?? "(none)"}, expected {numeric}.");
        }

        var informational = StringAttribute(reader, assembly, "AssemblyInformationalVersionAttribute");
        if (informational is null
            || (informational != version.Version
                && !informational.StartsWith($"{version.Version}+", StringComparison.Ordinal)))
        {
            problems.Add(
                $"{location} has informational version {informational ?? "(none)"}, expected {version.Version}.");
        }
    }

    /// <summary>The four-part version .NET gives an assembly for a SemVer version: <c>1.2.0-rc.1</c> is 1.2.0.0.</summary>
    private static Version NumericVersion(ReleaseVersion version)
    {
        var core = Version.Parse(version.Version.Split('-')[0]);
        return new Version(core.Major, core.Minor, core.Build, 0);
    }

    // The single string argument of an assembly attribute, read from metadata without loading the assembly.
    private static string? StringAttribute(MetadataReader reader, AssemblyDefinition assembly, string attributeName)
    {
        foreach (var handle in assembly.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference)
            {
                continue;
            }

            var constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference
                || !reader.StringComparer.Equals(
                    reader.GetTypeReference((TypeReferenceHandle)constructor.Parent).Name, attributeName))
            {
                continue;
            }

            var value = reader.GetBlobReader(attribute.Value);
            value.ReadUInt16(); // The prolog, 0x0001.
            return value.ReadSerializedString();
        }

        return null;
    }
}
