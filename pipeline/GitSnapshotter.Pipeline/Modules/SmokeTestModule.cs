using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModularPipelines.Context;
using ModularPipelines.Models;
using ModularPipelines.Modules;
using ModularPipelines.Options;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>
/// Checks that a published version can be used the way a user would use it: downloads both packages from
/// <see cref="BuildSettings.SmokeTestSource"/>, installs the .NET tool and runs it, builds and runs an app that
/// references the library, and checks that each downloaded package carries its third-party notices. Works in an empty
/// temporary directory, with its own NuGet packages folder and only that feed, so nothing comes from this build or
/// from the machine's NuGet cache. When <see cref="BuildSettings.SmokeTestReleaseDirectory"/> is set, also checks that
/// the downloaded packages hold exactly the files of the packages attached to the GitHub release. Retries the downloads for up to <see cref="BuildSettings.SmokeTestWaitMinutes"/>:
/// nuget.org takes a few minutes to list a package after it is pushed.
/// </summary>
public sealed class SmokeTestModule(IOptions<BuildSettings> settings) : Module<string>
{
    public const string LibraryId = "GitSnapshotter";

    public const string ToolId = "GitSnapshotter.Tool";

    private const string ToolCommand = "gitsnapshotter";

    /// <summary>The repository signature nuget.org adds to every package it accepts.</summary>
    internal const string RepositorySignature = ".signature.p7s";

    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    protected override async Task<string?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var version = settings.Value.Version
            ?? throw new InvalidOperationException("Set the version to smoke test (Build__Version).");
        var source = settings.Value.SmokeTestSource;
        var deadline = Stopwatch.StartNew();
        var wait = TimeSpan.FromMinutes(settings.Value.SmokeTestWaitMinutes);

        // Any git repository will do for the tool and the app to take a snapshot of; this one is at hand.
        var repository = Paths.Root(context);

        var work = Directory.CreateTempSubdirectory("gitsnapshotter-smoke-").FullName;
        try
        {
            var packages = Path.Combine(work, "packages");
            var tools = Path.Combine(work, "tools");
            var app = Path.Combine(work, "app");
            var nugetConfig = Path.Combine(work, "nuget.config");

            await File.WriteAllTextAsync(nugetConfig, NuGetConfig(source), new UTF8Encoding(false), cancellationToken);
            Directory.CreateDirectory(app);
            await File.WriteAllTextAsync(
                Path.Combine(app, "SmokeTest.csproj"),
                LibraryProject(version, $"net{Environment.Version.Major}.0"),
                new UTF8Encoding(false),
                cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(app, "Program.cs"), LibraryProgram, new UTF8Encoding(false), cancellationToken);

            context.Logger.LogInformation("Smoke testing {Version} from {Source} in {Directory}", version, source, work);

            // The tool: install it as a user would, then run it.
            await RetryAsync("install " + ToolId, attempt => Dotnet(
                work,
                attempt,
                ["tool", "install", ToolId, "--version", version, "--tool-path", tools, "--configfile", nugetConfig]));

            var toolVersion = await Run(Path.Combine(tools, OperatingSystem.IsWindows() ? ToolCommand + ".exe" : ToolCommand), ["--version"]);
            CheckToolVersion(toolVersion.StandardOutput, version);
            await Run(Path.Combine(tools, OperatingSystem.IsWindows() ? ToolCommand + ".exe" : ToolCommand), ["snapshot", repository]);

            // The library: restore an app that references it (the nuget.config above is picked up from the parent
            // directory), then build and run it.
            await RetryAsync("restore " + LibraryId, attempt => Dotnet(work, attempt, ["restore", app]));
            await Dotnet(work, 0, ["run", "--project", app, "--no-restore", "--", repository]);

            // The notices, in the packages as they were downloaded.
            var tool = FindPackage(tools, ToolId, version);
            var library = FindPackage(packages, LibraryId, version);
            CheckNotices(tool, ThirdPartyNoticesModule.ConsoleProduct);
            CheckNotices(library, ThirdPartyNoticesModule.LibraryProduct);

            // The packages on the feed are the ones the release published.
            if (!string.IsNullOrEmpty(settings.Value.SmokeTestReleaseDirectory))
            {
                var release = Path.GetFullPath(settings.Value.SmokeTestReleaseDirectory, repository);
                foreach (var (id, downloaded) in new[] { (ToolId, tool), (LibraryId, library) })
                {
                    ComparePackages(downloaded, Path.Combine(release, $"{id}.{version}.nupkg"));
                    context.Logger.LogInformation("{Id} {Version} on the feed matches the GitHub release", id, version);
                }
            }

            context.Logger.LogInformation("{Tool} and {Library} {Version} install and run", ToolId, LibraryId, version);
            return version;
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }

        Task<CommandResult> Dotnet(string directory, int attempt, string[] arguments) =>
            context.Shell.Command.ExecuteCommandLineTool(
                new GenericCommandLineToolOptions("dotnet") { Arguments = arguments },
                new CommandExecutionOptions
                {
                    WorkingDirectory = directory,
                    EnvironmentVariables = new Dictionary<string, string?>
                    {
                        // An empty packages folder, so every package is downloaded from the feed.
                        ["NUGET_PACKAGES"] = Path.Combine(directory, "packages"),

                        // A new HTTP cache on every attempt: NuGet caches "no such version" for 30 minutes.
                        ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(directory, $"http-cache-{attempt}"),
                        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                        ["DOTNET_NOLOGO"] = "1",
                    },
                },
                cancellationToken);

        Task<CommandResult> Run(string tool, string[] arguments) =>
            context.Shell.Command.ExecuteCommandLineTool(
                new GenericCommandLineToolOptions(tool) { Arguments = arguments },
                new CommandExecutionOptions { WorkingDirectory = work },
                cancellationToken);

        async Task RetryAsync(string what, Func<int, Task> action)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await action(attempt);
                    return;
                }
                catch (Exception exception) when (exception is not OperationCanceledException && deadline.Elapsed + RetryInterval < wait)
                {
                    context.Logger.LogWarning(
                        "Could not {What} {Version} (attempt {Attempt}); the feed may not list it yet. Retrying in {Delay}.",
                        what,
                        version,
                        attempt,
                        RetryInterval);
                    await Task.Delay(RetryInterval, cancellationToken);
                }
            }
        }
    }

    /// <summary>A nuget.config that uses <paramref name="source"/> only, so no package comes from another feed.</summary>
    internal static string NuGetConfig(string source) =>
        new XDocument(
            new XElement(
                "configuration",
                new XElement(
                    "packageSources",
                    new XElement("clear"),
                    new XElement("add", new XAttribute("key", "smoke-test"), new XAttribute("value", source)))))
            .ToString();

    /// <summary>An app that references exactly <paramref name="version"/> of the library.</summary>
    internal static string LibraryProject(string version, string targetFramework) =>
        new XDocument(
            new XElement(
                "Project",
                new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement(
                    "PropertyGroup",
                    new XElement("OutputType", "Exe"),
                    new XElement("TargetFramework", targetFramework),
                    new XElement("ImplicitUsings", "enable"),
                    new XElement("Nullable", "enable")),
                new XElement(
                    "ItemGroup",
                    new XElement(
                        "PackageReference",
                        new XAttribute("Include", LibraryId),
                        new XAttribute("Version", $"[{version}]")))))
            .ToString();

    // Takes a snapshot, which loads LibGit2Sharp and its native libgit2 from the package's dependencies.
    private const string LibraryProgram = """
        var snapshot = GitSnapshotter.GitRepository.GetSnapshot(args[0]);
        Console.WriteLine($"Snapshot of {args[0]}: {snapshot.Branches.Count} branches, {snapshot.Tags.Count} tags");
        """;

    /// <summary>Checks that <c>--version</c> printed <paramref name="version"/>, with or without build metadata.</summary>
    /// <exception cref="InvalidOperationException">The tool reported another version.</exception>
    internal static void CheckToolVersion(string output, string version)
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
    internal static string FindPackage(string directory, string id, string version)
    {
        var name = $"{id}.{version}.nupkg";
        return Directory.EnumerateFiles(directory, "*.nupkg", SearchOption.AllDirectories)
            .FirstOrDefault(file => string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"{name} was not downloaded to {directory}.", name);
    }

    /// <summary>
    /// Checks that <paramref name="downloaded"/> holds the same files, byte for byte, as <paramref name="released"/>.
    /// The files themselves differ: nuget.org adds its <see cref="RepositorySignature"/>, which is left out.
    /// </summary>
    /// <exception cref="InvalidOperationException">A file is missing, extra or different.</exception>
    internal static void ComparePackages(string downloaded, string released)
    {
        if (!File.Exists(released))
        {
            throw new FileNotFoundException($"The release has no {Path.GetFileName(released)}.", released);
        }

        var expected = Entries(released);
        var actual = Entries(downloaded);
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

        static Dictionary<string, byte[]> Entries(string package)
        {
            using var zip = ZipFile.OpenRead(package);
            var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var entry in zip.Entries)
            {
                using var stream = entry.Open();
                using var content = new MemoryStream();
                stream.CopyTo(content);
                entries[entry.FullName] = content.ToArray();
            }

            return entries;
        }
    }

    /// <summary>
    /// Checks that <paramref name="package"/> has <see cref="ThirdPartyNotices.FileName"/> at its root, written for
    /// <paramref name="product"/> and listing at least one package.
    /// </summary>
    /// <exception cref="InvalidOperationException">The notices are missing or don't look like the generated file.</exception>
    internal static void CheckNotices(string package, string product)
    {
        using var zip = ZipFile.OpenRead(package);
        var entry = zip.GetEntry(ThirdPartyNotices.FileName)
            ?? throw new InvalidOperationException($"{Path.GetFileName(package)} has no {ThirdPartyNotices.FileName}.");

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var notices = reader.ReadToEnd();

        if (!notices.StartsWith(ThirdPartyNotices.Heading(product), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{ThirdPartyNotices.FileName} in {Path.GetFileName(package)} is not the notices for {product}.");
        }

        if (!notices.Split('\n').Any(line => line.StartsWith(ThirdPartyNotices.ListItem, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"{ThirdPartyNotices.FileName} in {Path.GetFileName(package)} lists no packages.");
        }
    }
}
