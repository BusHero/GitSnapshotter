using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModularPipelines.Context;
using ModularPipelines.GitHub.Extensions;
using ModularPipelines.Models;
using ModularPipelines.Modules;
using ModularPipelines.Options;

using Octokit;

using static GitSnapshotter.Pipeline.SmokeTestChecks;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>
/// Checks that a published version can be used the way a user would use it. Works in an empty temporary directory,
/// with its own NuGet packages folder; our packages come only from <see cref="BuildSettings.SmokeTestSource"/> and
/// everything else from <see cref="BuildSettings.SmokeTestPreviousSource"/>, so nothing comes from this build or from
/// the machine's NuGet cache. It:
/// <list type="bullet">
/// <item>installs the .NET tool, checks its <c>--version</c> (and that of each framework's build in it), and has it
/// put a changed repository, working tree included, back the way a snapshot found it, from a snapshot taken by this
/// version and one taken by the previous release;</item>
/// <item>upgrades the tool from the previous release, then uninstalls it; installs it as a local tool from a tool
/// manifest; and runs it with <c>dotnet tool exec</c> (<c>dnx</c>);</item>
/// <item>builds an app that uses the library as a test would (snapshot, <c>Preserve</c>, JSON, <c>Restore</c>), and
/// runs it on every framework the library is built for (.NET Framework on Windows only) and on the newest
/// runtime;</item>
/// <item>checks that both packages carry their third-party notices, hold nothing but what they should, are signed,
/// and that neither the library nor anything the tool ships has a known vulnerability;</item>
/// <item>checks the library's API has no breaking change from the previous release (unless the changelog says so),
/// and that no part of either package grew by more than <see cref="BuildSettings.SmokeTestMaxGrowthPercent"/>;</item>
/// <item>checks that the feed shows the packages' metadata and serves their readme and icon;</item>
/// <item>checks that the symbol server has the PDB of each of our assemblies; that our assemblies were built
/// deterministically; and that Source Link maps every source file to the released commit;</item>
/// <item>checks the GitHub release, and that the published installation page matches the release;</item>
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
        var previousSource = settings.Value.SmokeTestPreviousSource;
        var deadline = Stopwatch.StartNew();
        var wait = TimeSpan.FromMinutes(settings.Value.SmokeTestWaitMinutes);
        var root = Paths.Root(context);
        var release = string.IsNullOrEmpty(settings.Value.SmokeTestReleaseDirectory)
            ? null
            : Path.GetFullPath(settings.Value.SmokeTestReleaseDirectory, root);
        var commit = string.IsNullOrEmpty(settings.Value.SmokeTestCommit) ? null : settings.Value.SmokeTestCommit;
        var currentRuntime = $"net{Environment.Version.Major}.0";
        var toolFile = OperatingSystem.IsWindows() ? ToolCommand + ".exe" : ToolCommand;

        // This repository, for read-only snapshots; the round trips use a repository of their own.
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
            var previousConfig = Path.Combine(work, "previous", "nuget.config");

            await Write(nugetConfig, NuGetConfig(source, previousSource, settings.Value.SmokeTestSourceUsername, settings.Value.SmokeTestSourcePassword));
            Directory.CreateDirectory(Path.GetDirectoryName(previousConfig)!);
            await Write(previousConfig, NuGetConfig(previousSource));
            Directory.CreateDirectory(app);
            await Write(project, LibraryProject(version, [currentRuntime]));
            await Write(Path.Combine(app, "Program.cs"), LibraryProgram);

            context.Logger.LogInformation("Smoke testing {Version} from {Source} in {Directory}", version, source, work);

            // The previous releases, from the feed users have them from.
            using var previousIndex = JsonDocument.Parse(await http.GetStringAsync(new Uri(previousSource), cancellationToken));
            var previousFeed = ServiceResource(previousIndex.RootElement, "PackageBaseAddress/3.0.0").TrimEnd('/');
            var previousTool = await PreviousVersionAsync(ToolId);
            var previousLibrary = await PreviousVersionAsync(LibraryId);

            // The tool: install it as a user would, then run it.
            await RetryAsync($"install {ToolId}", attempt => Dotnet(
                attempt, ["tool", "install", ToolId, "--version", version, "--tool-path", tools, "--configfile", nugetConfig]));

            var tool = Path.Combine(tools, toolFile);
            CheckToolVersion((await Run(tool, ["--version"])).StandardOutput, version);
            await Run(tool, ["snapshot", repository]);

            string? oldTool = null;
            if (previousTool is not null)
            {
                var oldTools = Path.Combine(work, "previous", "tools");
                await Dotnet(0, ["tool", "install", ToolId, "--version", previousTool, "--tool-path", oldTools, "--configfile", previousConfig]);
                oldTool = Path.Combine(oldTools, toolFile);
            }

            var roundTrip = await RoundTripAsync(tool, oldTool);
            await UpgradeAsync();
            await LocalToolAsync();
            await ToolExecAsync();

            // The library: restore an app that references it (the nuget.config above is picked up from the parent
            // directory), then build and run it on every framework the package has assemblies for.
            await RetryAsync($"restore {LibraryId}", attempt => Dotnet(attempt, ["restore", app]));

            var toolPackage = FindPackage(tools, ToolId, version);
            var libraryPackage = FindPackage(packages, LibraryId, version);
            var toolEntries = ReadEntries(toolPackage);
            var libraryEntries = ReadEntries(libraryPackage);

            // dotnet tool install runs the build that best matches the newest runtime; run every other one as well,
            // as a machine with an older runtime would.
            foreach (var framework in ToolFrameworks(toolEntries.Keys))
            {
                var build = Path.Combine(Path.GetDirectoryName(toolPackage)!, "tools", framework, "any", ConsoleAssembly);
                CheckToolVersion((await Dotnet(0, [build, "--version"])).StandardOutput, version);
                await Dotnet(0, [build, "snapshot", repository]);
                context.Logger.LogInformation("The tool's {Framework} build runs", framework);
            }

            var frameworks = TestFrameworks(LibFrameworks(libraryEntries.Keys), currentRuntime, OperatingSystem.IsWindows());
            await Write(project, LibraryProject(version, frameworks));
            await RetryAsync($"restore the app for {string.Join(", ", frameworks)} with", attempt => Dotnet(attempt, ["restore", app]));
            foreach (var framework in frameworks)
            {
                await Dotnet(0, ["run", "--project", app, "--framework", framework, "--no-restore", "--", roundTrip]);
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

            if (settings.Value.SmokeTestVerifySignatures)
            {
                await Dotnet(0, ["nuget", "verify", "--all", toolPackage, libraryPackage]);
                context.Logger.LogInformation("Both packages are signed");
            }

            // Known vulnerabilities in the library and its dependencies, and in every package the tool ships.
            await AuditAsync("audit-library", new Dictionary<string, string> { [LibraryId] = version });
            await AuditAsync("audit-tool", toolNoticed);
            context.Logger.LogInformation("Neither package, nor anything they ship or depend on, has a known vulnerability");

            // Compared with the previous release: the library's API, and both packages' size.
            var previousLibraryPackage = previousLibrary is null ? null : await DownloadPreviousAsync(LibraryId, previousLibrary);
            var previousToolPackage = previousTool is null ? null : await DownloadPreviousAsync(ToolId, previousTool);
            if (previousLibraryPackage is not null)
            {
                await CheckApiCompatibilityAsync(libraryPackage, previousLibraryPackage, previousLibrary!);
            }

            var growth = new List<string>();
            foreach (var (id, package, previous) in new[] { (ToolId, toolEntries, previousToolPackage), (LibraryId, libraryEntries, previousLibraryPackage) })
            {
                if (previous is not null)
                {
                    growth.AddRange(CheckGrowth(package, ReadEntries(previous), settings.Value.SmokeTestMaxGrowthPercent).Select(problem => $"{id}: {problem}"));
                }
            }

            if (growth.Count > 0)
            {
                throw new InvalidOperationException($"The packages grew more than {settings.Value.SmokeTestMaxGrowthPercent}%: {string.Join("; ", growth)}");
            }

            context.Logger.LogInformation("Neither package grew more than {Percent}% since the previous release", settings.Value.SmokeTestMaxGrowthPercent);

            // What the feed shows about them.
            if (settings.Value.SmokeTestFeedMetadata)
            {
                using var serviceIndex = JsonDocument.Parse(await http.GetStringAsync(new Uri(source), cancellationToken));
                foreach (var (id, entries) in new[] { (ToolId, toolEntries), (LibraryId, libraryEntries) })
                {
                    await RetryAsync($"read the metadata of {id}", _ => CheckFeedMetadataAsync(serviceIndex.RootElement, id, entries));
                }
            }

            // The symbols: each of our assemblies' PDB, from the symbol server, the release's symbol package or the
            // package itself (the tool carries its PDBs).
            var expectedCommit = commit ?? RepositoryCommit(Nuspec(libraryEntries))
                ?? throw new InvalidOperationException($"{LibraryId} {version} doesn't say which commit it was built from.");
            var assemblies = new List<(string Name, byte[] Assembly, byte[]? Pdb)>();
            foreach (var (id, entries) in new[] { (ToolId, toolEntries), (LibraryId, libraryEntries) })
            {
                var symbols = release is null ? null : Path.Combine(release, $"{id}.{version}.snupkg");
                var symbolEntries = symbols is not null && File.Exists(symbols) ? ReadEntries(symbols) : null;
                foreach (var assembly in OwnAssemblies(entries.Keys))
                {
                    var name = $"{id} {assembly}";
                    var pdb = (symbolEntries is null ? null : ReleasedPdb(symbolEntries, assembly)) ?? ReleasedPdb(entries, assembly);
                    if (settings.Value.SmokeTestSymbols && !string.IsNullOrEmpty(settings.Value.SmokeTestSymbolServer))
                    {
                        var released = pdb;
                        await RetryAsync(
                            $"download the symbols of {name}",
                            async _ => pdb = await CheckSymbolsAsync(entries[assembly], name, released));
                    }

                    assemblies.Add((name, entries[assembly], pdb));
                }
            }

            await CheckSourcesAsync(assemblies, expectedCommit);

            if (settings.Value.SmokeTestGitHubRelease)
            {
                await CheckGitHubReleaseAsync(expectedCommit);
            }

            if (settings.Value.SmokeTestDocs && !string.IsNullOrEmpty(settings.Value.SmokeTestDocsPage))
            {
                await CheckDocsAsync(settings.Value.SmokeTestDocsPage, LibFrameworks(libraryEntries.Keys), ToolFrameworks(toolEntries.Keys));
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

            async Task<string> RoundTripAsync(string tool, string? previous)
            {
                // A repository of its own, so the round trip can change it freely: commits, a tag, branches, and a
                // working tree with a modified, a staged and an untracked file.
                var repo = Path.Combine(work, "repository");
                var file = Path.Combine(repo, "file.txt");
                Directory.CreateDirectory(repo);
                await File.WriteAllTextAsync(Path.Combine(work, "gitconfig"), "", cancellationToken);

                await Git(repo, "init", "--initial-branch=main");
                await File.WriteAllTextAsync(file, "one\n", cancellationToken);
                await Git(repo, "add", "file.txt");
                await Git(repo, "commit", "--message", "one");
                await Git(repo, "tag", "v1");
                await Git(repo, "branch", "feature");
                await File.WriteAllTextAsync(file, "one, modified\n", cancellationToken);
                await File.WriteAllTextAsync(Path.Combine(repo, "staged.txt"), "staged\n", cancellationToken);
                await Git(repo, "add", "staged.txt");
                await File.WriteAllTextAsync(Path.Combine(repo, "untracked.txt"), "untracked\n", cancellationToken);

                var before = await Snapshot(tool, "before");
                (string Path, string Json)? old = previous is null ? null : await Snapshot(previous, "previous");

                await ChangeAndRestore(before.Path, "a snapshot it took");
                if (old is not null)
                {
                    await ChangeAndRestore(old.Value.Path, "a snapshot the previous release took");
                }

                context.Logger.LogInformation(
                    "{Tool} restores a changed repository and working tree to its snapshot{Previous}",
                    ToolCommand,
                    old is null ? "" : $", and to one {ToolId} {previousTool} took");
                return repo;

                // Commits everything (so the restore loses nothing), checks out a new branch, moves the tag and deletes a
                // branch; then restores the snapshot and checks the repository is back as it was.
                async Task ChangeAndRestore(string snapshot, string what)
                {
                    await Git(repo, "add", "--all");
                    await Git(repo, "commit", "--message", "two");
                    await Git(repo, "checkout", "--quiet", "-b", "extra");
                    await File.WriteAllTextAsync(file, "three\n", cancellationToken);
                    await Git(repo, "commit", "--all", "--message", "three");
                    await Git(repo, "tag", "--force", "v1");
                    await Git(repo, "branch", "--delete", "--force", "feature");
                    if (SameSnapshot(before.Json, (await Snapshot(tool, "changed")).Json))
                    {
                        throw new InvalidOperationException($"{ToolCommand} snapshot didn't see the repository change.");
                    }

                    await Run(tool, ["restore", repo, snapshot]);
                    if (!SameSnapshot(before.Json, (await Snapshot(tool, "after")).Json))
                    {
                        throw new InvalidOperationException($"{ToolCommand} restore of {what} didn't put the repository back as it was.");
                    }

                    var staged = (await Git(repo, "diff", "--cached", "--name-only")).StandardOutput.Trim();
                    var status = (await Git(repo, "status", "--porcelain", "--untracked-files=all")).StandardOutput;
                    if (await File.ReadAllTextAsync(file, cancellationToken) != "one, modified\n"
                        || staged != "staged.txt"
                        || !status.Contains("?? untracked.txt", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"{ToolCommand} restore of {what} didn't put the working tree back:\n{status}");
                    }
                }

                async Task<(string Path, string Json)> Snapshot(string with, string name)
                {
                    var path = Path.Combine(work, $"{name}.json");
                    await Run(with, ["snapshot", repo, "--output", path, "--include-working-tree"]);
                    return (path, await File.ReadAllTextAsync(path, cancellationToken));
                }
            }

            async Task UpgradeAsync()
            {
                if (previousTool is null)
                {
                    context.Logger.LogInformation("No release before {Version} to upgrade from", version);
                    return;
                }

                var upgrade = Path.Combine(work, "upgrade");
                var upgraded = Path.Combine(upgrade, toolFile);
                await Dotnet(0, ["tool", "install", ToolId, "--version", previousTool, "--tool-path", upgrade, "--configfile", previousConfig]);
                await Dotnet(0, ["tool", "update", ToolId, "--version", version, "--tool-path", upgrade, "--configfile", nugetConfig]);
                CheckToolVersion((await Run(upgraded, ["--version"])).StandardOutput, version);

                await Dotnet(0, ["tool", "uninstall", ToolId, "--tool-path", upgrade]);
                if (File.Exists(upgraded))
                {
                    throw new InvalidOperationException($"dotnet tool uninstall left {upgraded} behind.");
                }

                context.Logger.LogInformation("{Tool} upgrades from {Previous} to {Version} and uninstalls", ToolId, previousTool, version);
            }

            // As a team uses it: the version pinned in .config/dotnet-tools.json, restored after a clone.
            async Task LocalToolAsync()
            {
                var local = Path.Combine(work, "local");
                Directory.CreateDirectory(Path.Combine(local, ".config"));
                await Write(Path.Combine(local, ".config", "dotnet-tools.json"), ToolManifest(version));
                await Dotnet(0, ["tool", "restore", "--configfile", nugetConfig], local);
                CheckToolVersion((await Dotnet(0, ["tool", "run", ToolCommand, "--version"], local)).StandardOutput, version);
                CheckToolVersion((await Dotnet(0, [ToolCommand, "--version"], local)).StandardOutput, version);
                context.Logger.LogInformation("{Tool} restores and runs as a local tool", ToolId);
            }

            // dnx: run without installing.
            async Task ToolExecAsync()
            {
                var output = (await Dotnet(0, ["tool", "exec", $"{ToolId}@{version}", "--configfile", nugetConfig, "--", "--version"])).StandardOutput;
                CheckToolVersion(output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last(), version);
                context.Logger.LogInformation("{Tool} runs with dotnet tool exec (dnx)", ToolId);
            }

            async Task<string?> PreviousVersionAsync(string id)
            {
                using var index = JsonDocument.Parse(await http.GetStringAsync(new Uri($"{previousFeed}/{Lower(id)}/index.json"), cancellationToken));
                var versions = index.RootElement.GetProperty("versions").EnumerateArray().Select(v => v.GetString()!).ToList();
                if (settings.Value.SmokeTestAboveReleases && ReleasedAtOrAbove(versions, version) is { } released)
                {
                    throw new InvalidOperationException(
                        $"{id} {version} sorts below the released {released}, so updates and --prerelease never pick it. Raise "
                        + "<VersionPrefix> in Source/Directory.Build.props above the last release.");
                }

                var previous = PreviousStable(versions, version);
                context.Logger.LogInformation("The release of {Id} before {Version}: {Previous}", id, version, previous ?? "none");
                return previous;
            }

            async Task<string> DownloadPreviousAsync(string id, string previous)
            {
                var path = Path.Combine(work, "previous", $"{id}.{previous}.nupkg");
                await File.WriteAllBytesAsync(
                    path,
                    await http.GetByteArrayAsync(new Uri($"{previousFeed}/{Lower(id)}/{Lower(previous)}/{Lower(id)}.{Lower(previous)}.nupkg"), cancellationToken),
                    cancellationToken);
                return path;
            }

            async Task CheckApiCompatibilityAsync(string current, string previous, string previousVersion)
            {
                var apiCompat = Path.Combine(work, "apicompat");
                await Dotnet(0, ["tool", "install", "Microsoft.DotNet.ApiCompat.Tool", "--tool-path", apiCompat, "--configfile", previousConfig]);

                var result = await context.Shell.Command.ExecuteCommandLineTool(
                    new GenericCommandLineToolOptions(Path.Combine(apiCompat, OperatingSystem.IsWindows() ? "apicompat.exe" : "apicompat"))
                    {
                        Arguments = ["package", current, "--baseline-package", previous],
                    },
                    new CommandExecutionOptions { WorkingDirectory = work, ThrowOnNonZeroExitCode = false },
                    cancellationToken);

                if (result.ExitCode == 0)
                {
                    context.Logger.LogInformation("{Library} has no breaking change from {Previous}", LibraryId, previousVersion);
                    return;
                }

                string? notes;
                try
                {
                    (_, notes) = Changelog.ReleaseNotes(
                        await File.ReadAllLinesAsync(Paths.Changelog(context), cancellationToken), ReleaseVersion.Parse(version));
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
                {
                    notes = null;
                }

                if (!AllowsBreakingChanges(notes))
                {
                    throw new InvalidOperationException(
                        $"{LibraryId} {version} breaks the API of {previousVersion}. If that is intended, list the breaking "
                        + $"changes in the version's {Changelog.FileName} section, under a heading like '### Breaking changes':\n"
                        + result.StandardOutput);
                }

                context.Logger.LogWarning(
                    "{Library} {Version} breaks the API of {Previous}, as its changelog says:\n{Output}",
                    LibraryId,
                    version,
                    previousVersion,
                    result.StandardOutput);
            }

            async Task CheckDocsAsync(string url, IEnumerable<string> libraryFrameworks, IEnumerable<string> toolFrameworks)
            {
                var problems = CheckDocs(PageText(await http.GetStringAsync(new Uri(url), cancellationToken)), libraryFrameworks, toolFrameworks);
                if (problems.Count > 0)
                {
                    throw new InvalidOperationException($"{url} doesn't match the release: {string.Join("; ", problems)}");
                }

                context.Logger.LogInformation("{Url} matches the release", url);
            }

            Task<CommandResult> Git(string directory, params string[] arguments) =>
                context.Shell.Command.ExecuteCommandLineTool(
                    new GenericCommandLineToolOptions("git") { Arguments = arguments },
                    new CommandExecutionOptions
                    {
                        WorkingDirectory = directory,
                        EnvironmentVariables = new Dictionary<string, string?>
                        {
                            // Left alone by the machine's and the user's git configuration (signing, hooks, defaults).
                            ["GIT_CONFIG_NOSYSTEM"] = "1",
                            ["GIT_CONFIG_GLOBAL"] = Path.Combine(work, "gitconfig"),
                            ["GIT_AUTHOR_NAME"] = "Smoke Test",
                            ["GIT_AUTHOR_EMAIL"] = "smoke-test@example.invalid",
                            ["GIT_COMMITTER_NAME"] = "Smoke Test",
                            ["GIT_COMMITTER_EMAIL"] = "smoke-test@example.invalid",
                        },
                    },
                    cancellationToken);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }

        Task Write(string path, string content) =>
            File.WriteAllTextAsync(path, content, new UTF8Encoding(false), cancellationToken);

        Task<CommandResult> Dotnet(int attempt, string[] arguments, string? directory = null) =>
            context.Shell.Command.ExecuteCommandLineTool(
                new GenericCommandLineToolOptions("dotnet") { Arguments = arguments },
                new CommandExecutionOptions
                {
                    WorkingDirectory = directory ?? work,
                    EnvironmentVariables = new Dictionary<string, string?>
                    {
                        // An empty packages folder, so every package is downloaded from the feed.
                        ["NUGET_PACKAGES"] = Path.Combine(work, "packages"),

                        // A new HTTP cache on every attempt: NuGet caches "no such version" for 30 minutes.
                        ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(work, $"http-cache-{attempt}"),

                        // Its own .NET CLI home: the machine's would remember local tools from earlier runs
                        // (toolResolverCache), pointing at packages folders long deleted.
                        ["DOTNET_CLI_HOME"] = Path.Combine(work, "cli-home"),
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

        async Task<byte[]> CheckSymbolsAsync(byte[] assembly, string name, byte[]? released)
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
            return pdb;
        }

        async Task AuditAsync(string name, IEnumerable<KeyValuePair<string, string>> audited)
        {
            var directory = Path.Combine(work, name);
            Directory.CreateDirectory(directory);
            await Write(Path.Combine(directory, $"{name}.csproj"), AuditProject(audited, $"net{Environment.Version.Major}.0"));
            await Dotnet(0, ["restore", directory]);
        }

        async Task CheckSourcesAsync(List<(string Name, byte[] Assembly, byte[]? Pdb)> assemblies, string expectedCommit)
        {
            var problems = new List<string>();
            var files = new Dictionary<string, SourceFile>(StringComparer.Ordinal);
            foreach (var (name, assembly, pdb) in assemblies)
            {
                problems.AddRange(CheckDeterministic(assembly, name));
                if (pdb is null)
                {
                    context.Logger.LogWarning("No PDB for {Name} to check its Source Link with", name);
                    continue;
                }

                var (sourceProblems, sources) = CheckSourceLink(pdb, name, expectedCommit);
                problems.AddRange(sourceProblems);
                foreach (var file in sources)
                {
                    files.TryAdd(file.Url, file);
                }
            }

            if (problems.Count > 0)
            {
                throw new InvalidOperationException($"The assemblies' builds can't be traced to their sources: {string.Join("; ", problems)}");
            }

            await RetryAsync($"download the {files.Count} source files Source Link names for", async _ =>
            {
                var mismatched = new List<string>();
                foreach (var file in files.Values)
                {
                    using var response = await http.GetAsync(new Uri(file.Url), cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        mismatched.Add($"{file.Url} returns {(int)response.StatusCode}");
                    }
                    else if (!SourceMatches(await response.Content.ReadAsByteArrayAsync(cancellationToken), file))
                    {
                        mismatched.Add($"{file.Url} is not the {file.Document} the assemblies were built from");
                    }
                }

                if (mismatched.Count > 0)
                {
                    throw new InvalidOperationException(string.Join("; ", mismatched));
                }
            });

            context.Logger.LogInformation(
                "Our assemblies are deterministic, and Source Link maps their {Count} source files to {Commit}", files.Count, expectedCommit);
        }

        async Task CheckGitHubReleaseAsync(string expectedCommit)
        {
            var releaseVersion = ReleaseVersion.Parse(version);
            var github = context.GitHub();
            var owner = github.RepositoryInfo.Owner
                ?? throw new InvalidOperationException("The repository has no GitHub remote to find the release on.");
            var name = github.RepositoryInfo.RepositoryName!;
            var client = github.Client;

            var published = await client.Repository.Release.Get(owner, name, releaseVersion.Tag);
            var latest = await client.Repository.Release.GetLatest(owner, name);
            string? tagCommit;
            try
            {
                tagCommit = (await client.Repository.Commit.Get(owner, name, $"refs/tags/{releaseVersion.Tag}")).Sha;
            }
            catch (Exception exception) when (exception is NotFoundException or ApiValidationException)
            {
                tagCommit = null;
            }

            var (_, notes) = Changelog.ReleaseNotes(
                await File.ReadAllLinesAsync(Paths.Changelog(context), cancellationToken), releaseVersion);
            var problems = SmokeTestChecks.CheckGitHubRelease(
                new GitHubRelease(
                    published.Draft,
                    published.Prerelease,
                    published.Body,
                    [.. published.Assets.Select(asset => asset.Name)],
                    tagCommit,
                    latest.TagName),
                releaseVersion,
                expectedCommit,
                notes);

            if (problems.Count > 0)
            {
                throw new InvalidOperationException($"The GitHub release {releaseVersion.Tag} is wrong: {string.Join("; ", problems)}");
            }

            context.Logger.LogInformation("The GitHub release {Tag} is published and complete", releaseVersion.Tag);
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
