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
/// exactly the expected packages were built at the release version: pushing to nuget.org can't be undone.
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

        var version = settings.Value.Version!;
        var publish = (await context.GetModule<PackModule>()).ValueOrDefault!;
        var release = Paths.ReleaseDirectory(context, settings.Value);

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

        foreach (var file in Directory.EnumerateFiles(release).Order(StringComparer.Ordinal))
        {
            context.Logger.LogInformation("{File} ({Bytes} bytes)", Path.GetFileName(file), new FileInfo(file).Length);
        }

        return release;
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
