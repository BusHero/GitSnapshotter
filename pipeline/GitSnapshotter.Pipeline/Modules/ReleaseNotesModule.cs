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
/// Writes the GitHub release description, the CHANGELOG.md section for the version, to
/// <see cref="BuildSettings.ReleaseNotesDirectory"/>/release-notes.md and returns its path. A prerelease with no
/// section of its own uses Unreleased; a stable release must have its own section.
/// </summary>
[DependsOn<ReleaseVersionModule>]
public sealed class ReleaseNotesModule(IOptions<BuildSettings> settings) : Module<string>
{
    public const string FileName = "release-notes.md";

    protected override ModuleConfiguration Configure() =>
        ModuleConfiguration.Create()
            .WithSkipWhen(() => settings.Value.IsRelease
                ? SkipDecision.DoNotSkip
                : SkipDecision.Skip("Not a release"))
            .Build();

    protected override async Task<string?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var release = (await context.GetModule<ReleaseVersionModule>()).ValueOrDefault!;
        var (section, notes, file) = await WriteAsync(
            Paths.Changelog(context), release, Paths.ReleaseNotesDirectory(context, settings.Value), cancellationToken);

        context.Logger.LogInformation(
            "Release description, from the '{Section}' section of CHANGELOG.md:\n{Notes}", section, notes);

        return file;
    }

    /// <summary>Writes the release description for <paramref name="release"/> to <paramref name="directory"/>.</summary>
    /// <returns>The CHANGELOG.md section the notes come from, the notes, and the file they were written to.</returns>
    /// <exception cref="InvalidOperationException">CHANGELOG.md has nothing to describe the release with.</exception>
    internal static async Task<(string Section, string Notes, string File)> WriteAsync(
        string changelog, ReleaseVersion release, string directory, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(changelog, cancellationToken);
        var (section, notes) = Changelog.ReleaseNotes(lines, release);

        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, FileName);
        await File.WriteAllTextAsync(file, notes + "\n", new UTF8Encoding(false), cancellationToken);

        return (section, notes, file);
    }
}
