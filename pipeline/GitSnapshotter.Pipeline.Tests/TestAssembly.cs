using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;

namespace GitSnapshotter.Pipeline.Tests;

/// <summary>Builds small assemblies and packages with the versions a test needs, as a build would stamp them.</summary>
internal static class TestAssembly
{
    /// <summary>
    /// An empty assembly named <paramref name="name"/>. The file version defaults to the assembly version, and the
    /// informational version to <paramref name="informationalVersion"/> when given.
    /// </summary>
    public static byte[] Create(
        string name, string assemblyVersion, string? fileVersion = null, string? informationalVersion = null)
    {
        var builder = new PersistedAssemblyBuilder(
            new AssemblyName(name) { Version = new Version(assemblyVersion) }, typeof(object).Assembly);
        builder.SetCustomAttribute(Attribute<AssemblyFileVersionAttribute>(fileVersion ?? assemblyVersion));
        if (informationalVersion is not null)
        {
            builder.SetCustomAttribute(Attribute<AssemblyInformationalVersionAttribute>(informationalVersion));
        }

        builder.DefineDynamicModule(name);

        using var stream = new MemoryStream();
        builder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Writes a package (a zip) holding <paramref name="files"/>, keyed by their path in the package.</summary>
    public static void WritePackage(string path, IReadOnlyDictionary<string, byte[]> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryName, content) in files)
        {
            using var entry = zip.CreateEntry(entryName).Open();
            entry.Write(content);
        }
    }

    private static CustomAttributeBuilder Attribute<TAttribute>(string value)
        where TAttribute : Attribute =>
        new(typeof(TAttribute).GetConstructor([typeof(string)])!, [value]);
}
