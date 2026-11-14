namespace GitSnapshotter.Pipeline;

/// <summary>Reads sections of CHANGELOG.md (Keep a Changelog format).</summary>
internal static class Changelog
{
    public const string FileName = "CHANGELOG.md";

    public const string Unreleased = "Unreleased";

    /// <summary>
    /// The lines under <c>## [<paramref name="name"/>]</c>, up to the next section or the link definitions, without
    /// blank lines at either end. Empty when there is no such section or it has nothing in it.
    /// </summary>
    public static string Section(IEnumerable<string> lines, string name)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var heading = $"## [{name}]";
        var section = lines
            .SkipWhile(line => !line.StartsWith(heading, StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal) && !IsLinkDefinition(line))
            .SkipWhile(string.IsNullOrWhiteSpace)
            .Reverse()
            .SkipWhile(string.IsNullOrWhiteSpace)
            .Reverse();

        return string.Join('\n', section);
    }

    /// <summary>
    /// The GitHub release description for <paramref name="release"/>: its own section, or for a prerelease with no
    /// section of its own, <see cref="Unreleased"/>.
    /// </summary>
    /// <returns>The name of the section the notes come from, and the notes.</returns>
    /// <exception cref="InvalidOperationException">
    /// There is nothing to describe the release with. A stable release must have a section of its own.
    /// </exception>
    public static (string Section, string Notes) ReleaseNotes(IReadOnlyCollection<string> lines, ReleaseVersion release)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(release);

        var notes = Section(lines, release.Version);
        if (notes.Length > 0)
        {
            return (release.Version, notes);
        }

        if (release.IsPrerelease)
        {
            notes = Section(lines, Unreleased);
            if (notes.Length > 0)
            {
                return (Unreleased, notes);
            }
        }

        throw new InvalidOperationException(release.IsPrerelease
            ? $"{FileName} has nothing under '## [{release.Version}]' or '## [{Unreleased}]' to describe this release"
            : $"{FileName} has no '## [{release.Version}]' section. Rename {Unreleased} to it before releasing.");
    }

    // "[Unreleased]: https://...", the link definitions at the end of the file.
    private static bool IsLinkDefinition(string line) =>
        line.StartsWith('[', StringComparison.Ordinal) && line.IndexOf("]: ", StringComparison.Ordinal) > 1;
}
