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
        var lines = await File.ReadAllLinesAsync(Paths.Changelog(context), cancellationToken);

        var from = release.Version;
        var notes = Changelog.Section(lines, release.Version);
        if (notes.Length == 0 && release.IsPrerelease)
        {
            from = Changelog.Unreleased;
            notes = Changelog.Section(lines, Changelog.Unreleased);
        }

        if (notes.Length == 0)
        {
            throw new InvalidOperationException(release.IsPrerelease
                ? $"CHANGELOG.md has nothing under '## [{release.Version}]' or '## [{Changelog.Unreleased}]' to describe this release"
                : $"CHANGELOG.md has no '## [{release.Version}]' section. Rename {Changelog.Unreleased} to it before releasing.");
        }

        var directory = Paths.ReleaseNotesDirectory(context, settings.Value);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, FileName);
        await File.WriteAllTextAsync(file, notes + "\n", new UTF8Encoding(false), cancellationToken);

        context.Logger.LogInformation(
            "Release description, from the '{Section}' section of CHANGELOG.md:\n{Notes}", from, notes);

        return file;
    }
}
