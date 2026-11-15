using System.Globalization;
using System.Text.RegularExpressions;

namespace GitSnapshotter.Pipeline;

/// <summary>The version a release is made at.</summary>
/// <param name="Version">The SemVer version, like <c>1.2.0</c> or <c>1.2.0-rc.1</c>.</param>
public sealed partial record ReleaseVersion(string Version) : IComparable<ReleaseVersion>
{
    /// <summary>The git tag (and GitHub release) the version is released under.</summary>
    public string Tag => $"v{Version}";

    /// <summary>Whether the version has a prerelease label.</summary>
    public bool IsPrerelease => Version.Contains('-', StringComparison.Ordinal);

    /// <summary>Parses a version given as <c>1.2.0</c> or <c>v1.2.0</c>.</summary>
    /// <exception cref="ArgumentException">The version is not <c>major.minor.patch[-prerelease]</c>.</exception>
    public static ReleaseVersion Parse(string input) =>
        TryParse(input) ?? throw new ArgumentException($"'{input}' is not <major>.<minor>.<patch>[-<prerelease>]", nameof(input));

    /// <summary>Parses a version given as <c>1.2.0</c> or <c>v1.2.0</c>; <c>null</c> when it is not one.</summary>
    public static ReleaseVersion? TryParse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var version = Normalize(input);
        return SemVer().IsMatch(version) ? new ReleaseVersion(version) : null;
    }

    /// <summary>Drops the <c>v</c> a version may be given with, as in a tag name.</summary>
    public static string Normalize(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.StartsWith('v', StringComparison.Ordinal) ? input[1..] : input;
    }

    /// <summary>The next patch release after this version's <c>major.minor.patch</c>: 1.2.0 and 1.2.0-rc.1 give 1.2.1.</summary>
    public ReleaseVersion NextPatch()
    {
        var (major, minor, patch, _) = Split(Version);
        return new ReleaseVersion($"{major}.{minor}.{patch + 1}");
    }

    /// <summary>SemVer 2.0 precedence: by number, then a prerelease before its release, then label by label.</summary>
    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var (major, minor, patch, labels) = Split(Version);
        var (otherMajor, otherMinor, otherPatch, otherLabels) = Split(other.Version);

        var order = major.CompareTo(otherMajor);
        order = order != 0 ? order : minor.CompareTo(otherMinor);
        order = order != 0 ? order : patch.CompareTo(otherPatch);
        if (order != 0)
        {
            return order;
        }

        // A version without a label is the release, which comes after its prereleases.
        if (labels.Length == 0 || otherLabels.Length == 0)
        {
            return (labels.Length == 0, otherLabels.Length == 0) switch
            {
                (true, true) => 0,
                (true, false) => 1,
                _ => -1,
            };
        }

        for (var i = 0; i < Math.Min(labels.Length, otherLabels.Length); i++)
        {
            order = CompareLabel(labels[i], otherLabels[i]);
            if (order != 0)
            {
                return order;
            }
        }

        return labels.Length.CompareTo(otherLabels.Length);
    }

    public static bool operator <(ReleaseVersion? left, ReleaseVersion? right) => Compare(left, right) < 0;

    public static bool operator <=(ReleaseVersion? left, ReleaseVersion? right) => Compare(left, right) <= 0;

    public static bool operator >(ReleaseVersion? left, ReleaseVersion? right) => Compare(left, right) > 0;

    public static bool operator >=(ReleaseVersion? left, ReleaseVersion? right) => Compare(left, right) >= 0;

    private static int Compare(ReleaseVersion? left, ReleaseVersion? right) =>
        left is null ? (right is null ? 0 : -1) : left.CompareTo(right);

    // Numeric identifiers compare as numbers and come before alphanumeric ones, which compare in ASCII order.
    private static int CompareLabel(string left, string right)
    {
        var leftIsNumber = long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
        var rightIsNumber = long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);

        return (leftIsNumber, rightIsNumber) switch
        {
            (true, true) => leftNumber.CompareTo(rightNumber),
            (true, false) => -1,
            (false, true) => 1,
            _ => string.CompareOrdinal(left, right),
        };
    }

    private static (int Major, int Minor, int Patch, string[] Labels) Split(string version)
    {
        var parts = version.Split('-', 2);
        var numbers = parts[0].Split('.').Select(n => int.Parse(n, CultureInfo.InvariantCulture)).ToArray();
        return (numbers[0], numbers[1], numbers[2], parts.Length > 1 ? parts[1].Split('.') : []);
    }

    // SemVer 2.0: MAJOR.MINOR.PATCH with an optional -prerelease label (no build metadata).
    [GeneratedRegex(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?\z")]
    private static partial Regex SemVer();
}
