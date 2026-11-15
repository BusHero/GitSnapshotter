using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GitSnapshotter.Pipeline;

/// <summary>The version a build stamps on everything it builds: the assemblies, both packages and the console app.</summary>
/// <param name="Release">The version the build can be released as, like <c>0.4.1</c>.</param>
/// <param name="Build">Which build it is: the CI run number, or <see cref="PackageVersion.LocalBuild"/>.</param>
public sealed record BuildVersion(ReleaseVersion Release, string Build)
{
    /// <summary>The version NuGet knows the packages by, like <c>0.4.1</c>: NuGet ignores build metadata.</summary>
    public string Version => Release.Version;

    /// <summary>The version with its build metadata, like <c>0.4.1+57</c>: what the assemblies and the nuspec say.</summary>
    public string Full => $"{Release.Version}+{Build}";
}

/// <summary>
/// Works out a build's version. Every build is a release candidate, versioned as it would be released, plus the build
/// it came from as SemVer build metadata: <c>0.4.1+57</c>. Releasing a candidate publishes those very packages.
/// </summary>
internal static partial class PackageVersion
{
    /// <summary>The build metadata of a build that isn't a CI run.</summary>
    public const string LocalBuild = "local";

    /// <summary>
    /// The build metadata for <paramref name="buildNumber"/>: the CI run number, or <see cref="LocalBuild"/> without one.
    /// </summary>
    /// <exception cref="ArgumentException">The build number isn't valid SemVer build metadata.</exception>
    public static string Build(string? buildNumber)
    {
        if (string.IsNullOrWhiteSpace(buildNumber))
        {
            return LocalBuild;
        }

        var build = buildNumber.Trim();
        return BuildMetadata().IsMatch(build)
            ? build
            : throw new ArgumentException(
                $"'{buildNumber}' is not SemVer build metadata (dot-separated [0-9A-Za-z-] identifiers)", nameof(buildNumber));
    }

    /// <summary>
    /// The version the next release will have: <paramref name="prefix"/>, the <c>VersionPrefix</c> in
    /// Source/Directory.Build.props, unless a stable release at or above it is already tagged; then the patch after
    /// the newest such release. So nobody has to raise the prefix after each release, only to start a new minor or
    /// major version, or a prerelease.
    /// </summary>
    /// <param name="prefix">The version being worked towards.</param>
    /// <param name="tags">The repository's tags; those that aren't <c>v</c> and a version are ignored.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="prefix"/> is a prerelease that is released already, or sorts below a release: only a person
    /// can pick the next prerelease label, so the prefix has to be raised.
    /// </exception>
    public static ReleaseVersion Next(ReleaseVersion prefix, IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(tags);

        var released = tags
            .Where(tag => tag.StartsWith('v', StringComparison.Ordinal))
            .Select(ReleaseVersion.TryParse)
            .OfType<ReleaseVersion>()
            .ToList();

        if (prefix.IsPrerelease)
        {
            var above = released.Where(version => version >= prefix).Max();
            return above is null
                ? prefix
                : throw new InvalidOperationException(
                    $"<VersionPrefix> {prefix.Version} is not above the release {above.Version}; raise it in "
                    + "Source/Directory.Build.props");
        }

        var newestStable = released.Where(version => !version.IsPrerelease).Max();
        return newestStable is not null && newestStable >= prefix ? newestStable.NextPatch() : prefix;
    }

    /// <summary>The <c>VersionPrefix</c> a props file (Source/Directory.Build.props) sets.</summary>
    /// <exception cref="InvalidOperationException">The file sets no <c>VersionPrefix</c>.</exception>
    public static ReleaseVersion ReadPrefix(string propsFile)
    {
        var prefix = XDocument.Load(propsFile).Descendants("VersionPrefix").FirstOrDefault()?.Value.Trim();
        return string.IsNullOrEmpty(prefix)
            ? throw new InvalidOperationException($"{propsFile} sets no <VersionPrefix>.")
            : ReleaseVersion.Parse(prefix);
    }

    [GeneratedRegex(@"\A[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*\z")]
    private static partial Regex BuildMetadata();
}
