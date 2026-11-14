using System.Text.RegularExpressions;

namespace GitSnapshotter.Pipeline;

/// <summary>The version a release is made at.</summary>
/// <param name="Version">The SemVer version, like <c>1.2.0</c> or <c>1.2.0-rc.1</c>.</param>
public sealed partial record ReleaseVersion(string Version)
{
    /// <summary>The git tag (and GitHub release) the version is released under.</summary>
    public string Tag => $"v{Version}";

    /// <summary>Whether the version has a prerelease label.</summary>
    public bool IsPrerelease => Version.Contains('-', StringComparison.Ordinal);

    /// <summary>Parses a version given as <c>1.2.0</c> or <c>v1.2.0</c>.</summary>
    /// <exception cref="ArgumentException">The version is not <c>major.minor.patch[-prerelease]</c>.</exception>
    public static ReleaseVersion Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var version = Normalize(input);
        return SemVer().IsMatch(version)
            ? new ReleaseVersion(version)
            : throw new ArgumentException($"'{input}' is not <major>.<minor>.<patch>[-<prerelease>]", nameof(input));
    }

    /// <summary>Drops the <c>v</c> a version may be given with, as in a tag name.</summary>
    public static string Normalize(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.StartsWith('v', StringComparison.Ordinal) ? input[1..] : input;
    }

    // SemVer 2.0: MAJOR.MINOR.PATCH with an optional -prerelease label (no build metadata).
    [GeneratedRegex(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?\z")]
    private static partial Regex SemVer();
}
