using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModularPipelines.Context;
using ModularPipelines.Models;
using ModularPipelines.Modules;
using ModularPipelines.Options;

using static GitSnapshotter.Pipeline.SmokeTestChecks;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>
/// Checks that a published version can be used the way a user would use it. Works in an empty temporary directory,
/// with its own NuGet packages folder and only <see cref="BuildSettings.SmokeTestSource"/> as a feed, so nothing comes
/// from this build or from the machine's NuGet cache. It:
/// <list type="bullet">
/// <item>installs the .NET tool, checks its <c>--version</c> and takes a snapshot with it;</item>
/// <item>builds an app that references the library, and runs it on every framework the library is built for (.NET
/// Framework on Windows only) and on the newest runtime;</item>
/// <item>checks that both packages carry their third-party notices, and hold nothing but what they should;</item>
/// <item>checks that the feed shows the packages' metadata (license, readme, icon, repository and commit) and serves
/// their readme and icon;</item>
/// <item>checks that the symbol server (<see cref="BuildSettings.SmokeTestSymbolServer"/>) has the PDB of each of our
/// assemblies;</item>
/// <item>with <see cref="BuildSettings.SmokeTestReleaseDirectory"/>: checks the release assets against
/// SHA256SUMS.txt, that the feed's packages and the symbol server's PDBs are the release's, and that the console app
/// zip has its notices and runs.</item>
/// </list>
/// Retries the downloads for up to <see cref="BuildSettings.SmokeTestWaitMinutes"/>: nuget.org takes a few minutes to
/// list a package, its metadata and its symbols after it is pushed.
/// </summary>
public sealed class SmokeTestModule(IOptions<BuildSettings> settings) : Module<string>
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    protected override async Task<string?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var version = settings.Value.Version
            ?? throw new InvalidOperationException("Set the version to smoke test (Build__Version).");
        var source = settings.Value.SmokeTestSource;
        var deadline = Stopwatch.StartNew();
        var wait = TimeSpan.FromMinutes(settings.Value.SmokeTestWaitMinutes);
        var root = Paths.Root(context);
        var release = string.IsNullOrEmpty(settings.Value.SmokeTestReleaseDirectory)
            ? null
            : Path.GetFullPath(settings.Value.SmokeTestReleaseDirectory, root);
        var commit = string.IsNullOrEmpty(settings.Value.SmokeTestCommit) ? null : settings.Value.SmokeTestCommit;

        // Any git repository will do for the tool and the apps to take a snapshot of; this one is at hand.
        var repository = root;

        // The registration API serves gzip.
        using var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            CheckCertificateRevocationList = true,
        };
        using var http = new HttpClient(handler, disposeHandler: false);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GitSnapshotter.Pipeline");

        if (release is not null)
        {
            VerifyChecksums(release, ReleaseAssetsModule.ChecksumsFile);
            context.Logger.LogInformation("The release assets in {Directory} match their checksums", release);
        }

        var work = Directory.CreateTempSubdirectory("gitsnapshotter-smoke-").FullName;
        try
        {
            var packages = Path.Combine(work, "packages");
            var tools = Path.Combine(work, "tools");
            var app = Path.Combine(work, "app");
            var project = Path.Combine(app, "SmokeTest.csproj");
            var nugetConfig = Path.Combine(work, "nuget.config");
            var currentRuntime = $"net{Environment.Version.Major}.0";

            await Write(nugetConfig, NuGetConfig(source));
            Directory.CreateDirectory(app);
            await Write(project, LibraryProject(version, [currentRuntime]));
            await Write(Path.Combine(app, "Program.cs"), LibraryProgram);

            context.Logger.LogInformation("Smoke testing {Version} from {Source} in {Directory}", version, source, work);

            // The tool: install it as a user would, then run it.
            await RetryAsync($"install {ToolId}", attempt => Dotnet(
                attempt, ["tool", "install", ToolId, "--version", version, "--tool-path", tools, "--configfile", nugetConfig]));

            var tool = Path.Combine(tools, OperatingSystem.IsWindows() ? ToolCommand + ".exe" : ToolCommand);
            CheckToolVersion((await Run(tool, ["--version"])).StandardOutput, version);
            await Run(tool, ["snapshot", repository]);

            // The library: restore an app that references it (the nuget.config above is picked up from the parent
            // directory), then build and run it on every framework the package has assemblies for.
            await RetryAsync($"restore {LibraryId}", attempt => Dotnet(attempt, ["restore", app]));

            var toolPackage = FindPackage(tools, ToolId, version);
            var libraryPackage = FindPackage(packages, LibraryId, version);
            var toolEntries = ReadEntries(toolPackage);
            var libraryEntries = ReadEntries(libraryPackage);

            var frameworks = TestFrameworks(LibFrameworks(libraryEntries.Keys), currentRuntime, OperatingSystem.IsWindows());
            await Write(project, LibraryProject(version, frameworks));
            await RetryAsync($"restore the app for {string.Join(", ", frameworks)} with", attempt => Dotnet(attempt, ["restore", app]));
            foreach (var framework in frameworks)
            {
                await Dotnet(0, ["run", "--project", app, "--framework", framework, "--no-restore", "--", repository]);
            }

            // What the packages hold.
            var toolNoticed = CheckNotices(toolPackage, ThirdPartyNoticesModule.ConsoleProduct);
            CheckNotices(libraryPackage, ThirdPartyNoticesModule.LibraryProduct);
            CheckToolContents(toolEntries, toolNoticed);
            CheckLibraryContents(libraryEntries, XDocument.Load(Paths.Library(context)));
            context.Logger.LogInformation("Both packages have their third-party notices and nothing unexpected");

            if (release is not null)
            {
                foreach (var (id, package) in new[] { (ToolId, toolPackage), (LibraryId, libraryPackage) })
                {
                    ComparePackages(package, Path.Combine(release, $"{id}.{version}.nupkg"));
                }

                context.Logger.LogInformation("The feed's packages are the release's");

                await CheckConsoleZipAsync(Path.Combine(release, $"GitSnapshotter.Console_{version}.zip"));
            }

            // What the feed shows about them.
            using var serviceIndex = JsonDocument.Parse(await http.GetStringAsync(new Uri(source), cancellationToken));
            foreach (var (id, entries) in new[] { (ToolId, toolEntries), (LibraryId, libraryEntries) })
            {
                await RetryAsync($"read the metadata of {id}", _ => CheckFeedMetadataAsync(serviceIndex.RootElement, id, entries));
            }

            // The symbols.
            if (!string.IsNullOrEmpty(settings.Value.SmokeTestSymbolServer))
            {
                foreach (var (id, entries) in new[] { (ToolId, toolEntries), (LibraryId, libraryEntries) })
                {
                    var symbols = release is null ? null : Path.Combine(release, $"{id}.{version}.snupkg");
                    var symbolEntries = symbols is not null && File.Exists(symbols) ? ReadEntries(symbols) : null;
                    foreach (var assembly in OwnAssemblies(entries.Keys))
                    {
                        var released = symbolEntries is null ? null : ReleasedPdb(symbolEntries, assembly);
                        await RetryAsync(
                            $"download the symbols of {id} {assembly}",
                            _ => CheckSymbolsAsync(entries[assembly], $"{id} {assembly}", released));
                    }
                }
            }

            context.Logger.LogInformation("{Tool} and {Library} {Version} are published and work", ToolId, LibraryId, version);
            return version;

            async Task CheckConsoleZipAsync(string zip)
            {
                var console = Path.Combine(work, "console");
                await ZipFile.ExtractToDirectoryAsync(zip, console, cancellationToken);

                var notices = Path.Combine(console, ThirdPartyNotices.FileName);
                if (!File.Exists(notices))
                {
                    throw new InvalidOperationException($"{Path.GetFileName(zip)} has no {ThirdPartyNotices.FileName}.");
                }

                CheckNotices(
                    await File.ReadAllTextAsync(notices, cancellationToken),
                    ThirdPartyNoticesModule.ConsoleProduct,
                    Path.GetFileName(zip));

                // Through dotnet, so it runs on every OS: the zip's app host is built for Linux.
                var dll = Path.Combine(console, ConsoleAssembly);
                CheckToolVersion((await Dotnet(0, [dll, "--version"])).StandardOutput, version);
                await Dotnet(0, [dll, "snapshot", repository]);
                context.Logger.LogInformation("{Zip} has its notices and runs", Path.GetFileName(zip));
            }
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }

        Task Write(string path, string content) =>
            File.WriteAllTextAsync(path, content, new UTF8Encoding(false), cancellationToken);

        Task<CommandResult> Dotnet(int attempt, string[] arguments) =>
            context.Shell.Command.ExecuteCommandLineTool(
                new GenericCommandLineToolOptions("dotnet") { Arguments = arguments },
                new CommandExecutionOptions
                {
                    WorkingDirectory = work,
                    EnvironmentVariables = new Dictionary<string, string?>
                    {
                        // An empty packages folder, so every package is downloaded from the feed.
                        ["NUGET_PACKAGES"] = Path.Combine(work, "packages"),

                        // A new HTTP cache on every attempt: NuGet caches "no such version" for 30 minutes.
                        ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(work, $"http-cache-{attempt}"),
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

        async Task CheckFeedMetadataAsync(JsonElement serviceIndex, string id, IReadOnlyDictionary<string, byte[]> entries)
        {
            var nuspec = Nuspec(entries);
            var package = $"{Lower(id)}/{Lower(version)}";

            var registrations = ServiceResource(serviceIndex, "RegistrationsBaseUrl/3.6.0", "RegistrationsBaseUrl");
            using var leaf = JsonDocument.Parse(await http.GetStringAsync(
                new Uri($"{registrations.TrimEnd('/')}/{package}.json"), cancellationToken));
            using var catalog = JsonDocument.Parse(await http.GetStringAsync(
                new Uri(leaf.RootElement.GetProperty("catalogEntry").GetString()!), cancellationToken));

            var problems = CompareMetadata(nuspec, catalog.RootElement, commit).ToList();

            // nuget.org serves the readme and the icon from the package; they must be the package's files.
            var flat = ServiceResource(serviceIndex, "PackageBaseAddress/3.0.0").TrimEnd('/');
            foreach (var (element, endpoint) in new[] { ("readme", "readme"), ("icon", "icon") })
            {
                var file = nuspec.Elements().FirstOrDefault(e => e.Name.LocalName == element)?.Value.Trim();
                if (file is null)
                {
                    continue; // CompareMetadata reports it.
                }

                var url = $"{flat}/{package}/{endpoint}";
                var served = await http.GetByteArrayAsync(new Uri(url), cancellationToken);
                if (!entries.TryGetValue(file, out var packaged) || !packaged.SequenceEqual(served))
                {
                    problems.Add($"{url} is not the package's {file}");
                }
            }

            if (problems.Count > 0)
            {
                throw new InvalidOperationException($"{source} shows {id} {version} wrongly: {string.Join("; ", problems)}");
            }

            context.Logger.LogInformation("{Source} shows the metadata, readme and icon of {Id} {Version}", source, id, version);
        }

        async Task CheckSymbolsAsync(byte[] assembly, string name, byte[]? released)
        {
            var query = SymbolQueryFor(assembly, name);
            using var request = new HttpRequestMessage(
                HttpMethod.Get, new Uri($"{settings.Value.SmokeTestSymbolServer.TrimEnd('/')}/{query.Key}"));
            request.Headers.Add("SymbolChecksum", query.Checksum);

            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var pdb = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            CheckPdb(pdb, query);
            if (released is not null && !released.SequenceEqual(pdb))
            {
                throw new InvalidOperationException($"The symbol server's {query.PdbName} for {name} is not the release's.");
            }

            context.Logger.LogInformation("The symbol server has {Pdb} for {Name}", query.PdbName, name);
        }

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
                        "Could not {What} {Version} (attempt {Attempt}): {Error} It may not be published yet; retrying in {Delay}.",
                        what,
                        version,
                        attempt,
                        exception.Message,
                        RetryInterval);
                    await Task.Delay(RetryInterval, cancellationToken);
                }
            }
        }
    }
}
