using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModularPipelines.Attributes;
using ModularPipelines.Configuration;
using ModularPipelines.Context;
using ModularPipelines.Models;
using ModularPipelines.Modules;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>
/// On a release build, puts everything the GitHub release gets into <see cref="BuildSettings.ReleaseDirectory"/>: the
/// packages and their symbol packages, the console app as a zip, and SHA256SUMS.txt over all of them. Fails unless
/// exactly the expected packages were built at the release version, and our assemblies in them and in the console app
/// carry that version too (<see cref="ReleaseAssemblies"/>): pushing to nuget.org can't be undone.
/// </summary>
[DependsOn<PackModule>]
[DependsOn<PublishConsoleModule>]
[DependsOn<RunUnitTestsModule>]
public sealed class ReleaseAssetsModule(IOptions<BuildSettings> settings) : Module<string>
{
    public const string ChecksumsFile = "SHA256SUMS.txt";

    private static readonly string[] PackageIds = ["GitSnapshotter", "GitSnapshotter.Tool"];

    protected override ModuleConfiguration Configure() =>
        ModuleConfiguration.Create()
            .WithSkipWhen(() => settings.Value.IsRelease
                ? SkipDecision.DoNotSkip
                : SkipDecision.Skip("Not a release"))
            .Build();

    protected override async Task<string?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var files = await CreateAsync(
            (await context.GetModule<PackModule>()).ValueOrDefault!,
            Paths.ReleaseDirectory(context, settings.Value),
            settings.Value.Version!,
            cancellationToken);

        foreach (var file in files)
        {
            context.Logger.LogInformation("{File} ({Bytes} bytes)", Path.GetFileName(file), new FileInfo(file).Length);
        }

        return Paths.ReleaseDirectory(context, settings.Value);
    }

    /// <summary>
    /// Replaces <paramref name="release"/> with the release assets made from <paramref name="publish"/>, the publish
    /// directory: the packages, the console app zip and the checksums.
    /// </summary>
    /// <returns>The files in <paramref name="release"/>, in order.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="publish"/> doesn't hold exactly the expected packages at <paramref name="version"/>, or an
    /// assembly in it has another version.
    /// </exception>
    internal static async Task<IReadOnlyList<string>> CreateAsync(
        string publish, string release, string version, CancellationToken cancellationToken)
    {
        var expected = PackageIds.Select(id => $"{id}.{version}.nupkg").Order(StringComparer.Ordinal).ToList();
        var actual = Directory.EnumerateFiles(publish, "*.nupkg")
            .Select(file => Path.GetFileName(file))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected exactly: {string.Join(' ', expected)}. The pipeline produced: {string.Join(' ', actual)}");
        }

        var problems = ReleaseAssemblies.Verify(publish, ReleaseVersion.Parse(version));
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"The assemblies don't carry release version {version}:{Environment.NewLine}"
                + string.Join(Environment.NewLine, problems));
        }

        if (Directory.Exists(release))
        {
            Directory.Delete(release, recursive: true);
        }

        Directory.CreateDirectory(release);

        foreach (var package in Directory.EnumerateFiles(publish).Where(IsPackage))
        {
            File.Copy(package, Path.Combine(release, Path.GetFileName(package)));
        }

        ZipConsoleApp(publish, Path.Combine(release, $"GitSnapshotter.Console_{version}.zip"));
        await WriteChecksumsAsync(release, cancellationToken);

        return [.. Directory.EnumerateFiles(release).Order(StringComparer.Ordinal)];
    }

    private static bool IsPackage(string file) =>
        file.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
        || file.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase);

    // The publish directory without the packages: the console app and its third-party notices.
    private static void ZipConsoleApp(string publish, string zipFile)
    {
        using var zip = ZipFile.Open(zipFile, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(publish, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            if (!IsPackage(file))
            {
                // Keeps the file's Unix permissions, so the app host stays executable.
                zip.CreateEntryFromFile(file, Path.GetRelativePath(publish, file).Replace('\\', '/'), CompressionLevel.Optimal);
            }
        }
    }

    // In sha256sum's format, so the publish job can check the files with sha256sum --check.
    private static async Task WriteChecksumsAsync(string release, CancellationToken cancellationToken)
    {
        var checksums = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(release).Order(StringComparer.Ordinal))
        {
            var stream = File.OpenRead(file);
            await using (stream.ConfigureAwait(false))
            {
                var hash = await SHA256.HashDataAsync(stream, cancellationToken);
                checksums.Append(Convert.ToHexStringLower(hash)).Append("  ").Append(Path.GetFileName(file)).Append('\n');
            }
        }

        await File.WriteAllTextAsync(
            Path.Combine(release, ChecksumsFile), checksums.ToString(), new UTF8Encoding(false), cancellationToken);
    }
}
