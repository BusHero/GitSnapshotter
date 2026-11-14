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

    // "[Unreleased]: https://...", the link definitions at the end of the file.
    private static bool IsLinkDefinition(string line) =>
        line.StartsWith('[', StringComparison.Ordinal) && line.IndexOf("]: ", StringComparison.Ordinal) > 1;
}
