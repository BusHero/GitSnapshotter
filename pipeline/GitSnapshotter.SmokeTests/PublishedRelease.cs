using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

using TUnit.Core.Interfaces;

using static GitSnapshotter.SmokeTests.SmokeTestChecks;

namespace GitSnapshotter.SmokeTests;

/// <summary>
/// The version under test, as a user gets it, set up once for every test: in an empty temporary directory, with its
/// own NuGet packages folder and .NET CLI home, the tool installed and an app referencing the library restored. Our
/// packages come only from <see cref="SmokeTestSettings.Source"/>, everything else from
/// <see cref="SmokeTestSettings.PreviousSource"/>, so nothing comes from this build or the machine's caches.
/// </summary>
public sealed class PublishedRelease : IAsyncInitializer, IAsyncDisposable
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    private readonly Stopwatch _started = Stopwatch.StartNew();

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<string>>> _downloads = new(StringComparer.OrdinalIgnoreCase);

    private readonly HttpClientHandler _handler = new()
    {
        // The registration API serves gzip.
        AutomaticDecompression = DecompressionMethods.All,
        CheckCertificateRevocationList = true,

        // A revocation status that couldn't be checked fails no test; a revoked certificate still does.
        ServerCertificateCustomValidationCallback = (_, _, chain, errors) =>
            AcceptCertificate(errors, chain?.ChainStatus.Select(status => status.Status) ?? []),
    };

    public PublishedRelease()
    {
        Http = new HttpClient(_handler, disposeHandler: false);
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("GitSnapshotter.SmokeTests");
    }

    public SmokeTestSettings Settings { get; } = new();

    public string Version => Settings.Version;

    public HttpClient Http { get; }

    /// <summary>
    /// The temporary directory everything happens in. Made when the tests start, not when the fixture is: listing
    /// the tests creates it without starting or disposing it.
    /// </summary>
    public string Work { get; private set; } = "";

    /// <summary>Our packages from <see cref="SmokeTestSettings.Source"/>, the rest from the previous releases' feed.</summary>
    public string NuGetConfig => Path.Combine(Work, "nuget.config");

    /// <summary>The previous releases' feed only.</summary>
    public string PreviousConfig => Path.Combine(Work, "previous", "nuget.config");

    /// <summary>The installed tool's command.</summary>
    public string Tool => ToolIn(Path.Combine(Work, "tools"));

    /// <summary>The app that references the library (<c>app/SmokeTest.csproj</c>).</summary>
    public string App => Path.Combine(Work, "app");

    /// <summary>The newest runtime, which the tool and the app run on by default.</summary>
    public string CurrentRuntime { get; } = $"net{Environment.Version.Major}.0";

    /// <summary>The previous releases' flat container (PackageBaseAddress).</summary>
    public string PreviousFeed { get; private set; } = "";

    public IReadOnlyList<string> ToolVersions { get; private set; } = [];

    public IReadOnlyList<string> LibraryVersions { get; private set; } = [];

    /// <summary>The stable release of the tool before <see cref="Version"/>, if any.</summary>
    public string? PreviousTool => PreviousStable(ToolVersions, Version);

    /// <summary>The stable release of the library before <see cref="Version"/>, if any.</summary>
    public string? PreviousLibrary => PreviousStable(LibraryVersions, Version);

    public string ToolPackage { get; private set; } = "";

    public string LibraryPackage { get; private set; } = "";

    public IReadOnlyDictionary<string, byte[]> ToolEntries { get; private set; } = new Dictionary<string, byte[]>();

    public IReadOnlyDictionary<string, byte[]> LibraryEntries { get; private set; } = new Dictionary<string, byte[]>();

    /// <summary>The commit the packages must have been built from.</summary>
    public string Commit => Settings.Commit ?? RepositoryCommit(Nuspec(LibraryEntries))
        ?? throw new InvalidOperationException($"{LibraryId} {Version} doesn't say which commit it was built from.");

    /// <summary>The release assets directory, if <see cref="SmokeTestSettings.ReleaseDirectory"/> is set.</summary>
    public string? Release => Settings.ReleaseDirectory is { } release ? Path.GetFullPath(release, Settings.Root) : null;

    public async Task InitializeAsync()
    {
        Work = Directory.CreateTempSubdirectory("gitsnapshotter-smoke-").FullName;

        // Empty: git ignores the machine's and the user's configuration. Made here, before the tests run in parallel.
        await File.WriteAllTextAsync(Path.Combine(Work, "gitconfig"), "");
        Console.WriteLine($"Smoke testing {Version} from {Settings.Source} in {Work}");

        await WriteAsync(NuGetConfig, SmokeTestChecks.NuGetConfig(
            Settings.Source, Settings.PreviousSource, Settings.SourceUsername, Settings.SourcePassword));
        Directory.CreateDirectory(Path.GetDirectoryName(PreviousConfig)!);
        await WriteAsync(PreviousConfig, SmokeTestChecks.NuGetConfig(Settings.PreviousSource));

        using var previousIndex = JsonDocument.Parse(await Http.GetStringAsync(new Uri(Settings.PreviousSource)));
        PreviousFeed = ServiceResource(previousIndex.RootElement, "PackageBaseAddress/3.0.0").TrimEnd('/');
        ToolVersions = await VersionsAsync(ToolId);
        LibraryVersions = await VersionsAsync(LibraryId);
        Console.WriteLine($"The releases before {Version}: {ToolId} {PreviousTool ?? "none"}, {LibraryId} {PreviousLibrary ?? "none"}");

        // The tool, installed as a user would.
        var tools = Path.Combine(Work, "tools");
        await RetryAsync($"install {ToolId}", attempt => DotnetAsync(
            ["tool", "install", ToolId, "--version", Version, "--tool-path", tools, "--configfile", NuGetConfig], attempt: attempt));

        // An app referencing the library, for the newest runtime; the library tests add the others. The nuget.config
        // above is picked up from the parent directory.
        Directory.CreateDirectory(App);
        await WriteAsync(Path.Combine(App, "SmokeTest.csproj"), LibraryProject(Version, [CurrentRuntime]));
        await WriteAsync(Path.Combine(App, "Program.cs"), LibraryProgram);
        await RetryAsync($"restore {LibraryId}", attempt => DotnetAsync(["restore", App], attempt: attempt));

        ToolPackage = FindPackage(tools, ToolId, Version);
        LibraryPackage = FindPackage(Path.Combine(Work, "packages"), LibraryId, Version);
        ToolEntries = ReadEntries(ToolPackage);
        LibraryEntries = ReadEntries(LibraryPackage);
    }

    public ValueTask DisposeAsync()
    {
        Http.Dispose();
        _handler.Dispose();
        // Only a temporary directory: failing to remove it fails no test.
        if (Work.Length == 0)
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            DeleteDirectory(Work);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not delete {Work}: {exception.Message}");
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Deletes a directory with everything in it, read-only files included: git makes its object files read-only, and
    /// on Windows a read-only file can't be deleted (UnauthorizedAccessException).
    /// </summary>
    public static void DeleteDirectory(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }

    /// <summary>The tool's command in a tool path.</summary>
    public static string ToolIn(string toolPath) =>
        Path.Combine(toolPath, OperatingSystem.IsWindows() ? ToolCommand + ".exe" : ToolCommand);

    public static Task WriteAsync(string path, string content) =>
        File.WriteAllTextAsync(path, content, new UTF8Encoding(false));

    /// <summary>
    /// Runs dotnet with the empty packages folder (every package downloaded from the feeds), a new HTTP cache per
    /// <paramref name="attempt"/> (NuGet caches "no such version" for 30 minutes) and a .NET CLI home of its own (the
    /// machine's remembers local tools from earlier runs, in packages folders long deleted).
    /// </summary>
    public Task<CommandResult> DotnetAsync(string[] arguments, string? directory = null, int attempt = 0)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return Command.RunAsync(
            "dotnet",
            arguments,
            directory ?? Work,
            new Dictionary<string, string>
            {
                ["NUGET_PACKAGES"] = Path.Combine(Work, "packages"),
                ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(Work, $"http-cache-{attempt}"),
                ["DOTNET_CLI_HOME"] = Path.Combine(Work, "cli-home"),
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                ["DOTNET_NOLOGO"] = "1",

                // No MSBuild nodes or compiler server left running after a build: they inherit the command's output,
                // which then never ends.
                ["MSBUILDDISABLENODEREUSE"] = "1",
                ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
                ["UseSharedCompilation"] = "false",
            });
    }

    /// <summary>Runs an installed tool.</summary>
    public Task<CommandResult> RunAsync(string tool, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return Command.RunAsync(tool, arguments, Work);
    }

    /// <summary>Runs git, left alone by the machine's and the user's git configuration (signing, hooks, defaults).</summary>
    public async Task<CommandResult> GitAsync(string directory, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return await Command.RunAsync(
            "git",
            arguments,
            directory,
            new Dictionary<string, string>
            {
                ["GIT_CONFIG_NOSYSTEM"] = "1",
                ["GIT_CONFIG_GLOBAL"] = Path.Combine(Work, "gitconfig"),
                ["GIT_AUTHOR_NAME"] = "Smoke Test",
                ["GIT_AUTHOR_EMAIL"] = "smoke-test@example.invalid",
                ["GIT_COMMITTER_NAME"] = "Smoke Test",
                ["GIT_COMMITTER_EMAIL"] = "smoke-test@example.invalid",
            });
    }

    /// <summary>
    /// A new repository in <c>Work/&lt;name&gt;</c>: two branches, a tag, and a working tree with a modified, a
    /// staged and an untracked file.
    /// </summary>
    public async Task<string> CreateRepositoryAsync(string name)
    {
        var repo = Path.Combine(Work, name);
        Directory.CreateDirectory(repo);
        await GitAsync(repo, "init", "--initial-branch=main");
        await File.WriteAllTextAsync(Path.Combine(repo, "file.txt"), "one\n");
        await GitAsync(repo, "add", "file.txt");
        await GitAsync(repo, "commit", "--message", "one");
        await GitAsync(repo, "tag", "v1");
        await GitAsync(repo, "branch", "feature");
        await File.WriteAllTextAsync(Path.Combine(repo, "file.txt"), "one, modified\n");
        await File.WriteAllTextAsync(Path.Combine(repo, "staged.txt"), "staged\n");
        await GitAsync(repo, "add", "staged.txt");
        await File.WriteAllTextAsync(Path.Combine(repo, "untracked.txt"), "untracked\n");
        return repo;
    }

    /// <summary>Installs the previous release of the tool, from the previous releases' feed.</summary>
    public async Task<string> InstallPreviousToolAsync(string toolPath)
    {
        await DotnetAsync(["tool", "install", ToolId, "--version", PreviousTool!, "--tool-path", toolPath, "--configfile", PreviousConfig]);
        return ToolIn(toolPath);
    }

    /// <summary>
    /// Downloads a previous release's package from the previous releases' feed, once: tests running in parallel that
    /// ask for the same package share the download, and none sees a half-written file.
    /// </summary>
    public Task<string> DownloadPreviousAsync(string id, string version)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(version);

        return _downloads.GetOrAdd($"{id}.{version}", _ => new Lazy<Task<string>>(() => DownloadAsync(id, version))).Value;

        async Task<string> DownloadAsync(string id, string version)
        {
            var path = Path.Combine(Work, "previous", $"{id}.{version}.nupkg");
            var url = $"{PreviousFeed}/{Lower(id)}/{Lower(version)}/{Lower(id)}.{Lower(version)}.nupkg";

            // Written under another name, then moved: the package's name only ever holds the whole package.
            var partial = path + ".part";
            await File.WriteAllBytesAsync(partial, await Http.GetByteArrayAsync(new Uri(url)));
            File.Move(partial, path, overwrite: true);
            return path;
        }
    }

    /// <summary>
    /// Retries <paramref name="action"/> every 30 seconds until <see cref="SmokeTestSettings.Wait"/> has passed since
    /// the tests started: nuget.org takes a few minutes to list a package, its metadata and its symbols.
    /// </summary>
    public async Task RetryAsync(string what, Func<int, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await action(attempt);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException && _started.Elapsed + RetryInterval < Settings.Wait)
            {
                Console.WriteLine(
                    $"Could not {what} {Version} (attempt {attempt}): {exception.Message} It may not be published yet; retrying in {RetryInterval}.");
                await Task.Delay(RetryInterval);
            }
        }
    }

    private async Task<IReadOnlyList<string>> VersionsAsync(string id)
    {
        using var index = JsonDocument.Parse(await Http.GetStringAsync(new Uri($"{PreviousFeed}/{Lower(id)}/index.json")));
        return [.. index.RootElement.GetProperty("versions").EnumerateArray().Select(v => v.GetString()!)];
    }
}
