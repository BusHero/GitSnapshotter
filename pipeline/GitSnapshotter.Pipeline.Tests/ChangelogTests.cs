using TUnit.Core;

namespace GitSnapshotter.Pipeline.Tests;

public sealed class ChangelogTests
{
    private static readonly string[] Sample =
    [
        "# Changelog",
        "",
        "Intro text, with a [link](https://example.com).",
        "",
        "## [Unreleased]",
        "",
        "### Changed",
        "",
        "- An unreleased change.",
        "",
        "## [1.2.0] - 2026-12-01",
        "",
        "Summary of 1.2.0.",
        "",
        "### Added",
        "",
        "- A feature.",
        "  - A detail.",
        "",
        "",
        "## [1.2.0-rc.1] - 2026-11-20",
        "",
        "- The release candidate.",
        "",
        "## [1.1.0] - 2026-10-01",
        "",
        "",
        "[Unreleased]: https://github.com/BusHero/GitSnapshotter/compare/v1.2.0...HEAD",
        "[1.2.0]: https://github.com/BusHero/GitSnapshotter/compare/v1.1.0...v1.2.0",
    ];

    private static string Lines(params string[] lines) => string.Join('\n', lines);

    [Test]
    public async Task SectionIsTheLinesUnderItsHeadingWithoutSurroundingBlanks()
    {
        await Assert.That(Changelog.Section(Sample, "1.2.0"))
            .IsEqualTo(Lines("Summary of 1.2.0.", "", "### Added", "", "- A feature.", "  - A detail."));
    }

    [Test]
    public async Task SectionWithoutADateIsFound()
    {
        await Assert.That(Changelog.Section(Sample, Changelog.Unreleased))
            .IsEqualTo(Lines("### Changed", "", "- An unreleased change."));
    }

    [Test]
    public async Task SectionMatchesTheWholeVersion()
    {
        // "## [1.2.0]" must not pick up "## [1.2.0-rc.1]", nor the other way round.
        await Assert.That(Changelog.Section(Sample, "1.2.0-rc.1")).IsEqualTo("- The release candidate.");
    }

    [Test]
    public async Task LastSectionStopsAtTheLinkDefinitions()
    {
        string[] lines =
        [
            "## [1.0.0]",
            "- First release.",
            "",
            "[1.0.0]: https://example.com",
        ];

        await Assert.That(Changelog.Section(lines, "1.0.0")).IsEqualTo("- First release.");
    }

    [Test]
    public async Task LinksInTheTextDoNotEndTheSection()
    {
        string[] lines =
        [
            "## [1.0.0]",
            "[Docs](https://example.com) moved.",
            "- See [the guide][guide].",
        ];

        await Assert.That(Changelog.Section(lines, "1.0.0"))
            .IsEqualTo(Lines("[Docs](https://example.com) moved.", "- See [the guide][guide]."));
    }

    [Test]
    [Arguments("1.1.0")]
    [Arguments("9.9.9")]
    [Arguments("1.2")]
    public async Task MissingOrEmptySectionIsEmpty(string name)
    {
        await Assert.That(Changelog.Section(Sample, name)).IsEmpty();
    }

    [Test]
    public async Task ReleaseNotesUseTheVersionsOwnSection()
    {
        var (section, notes) = Changelog.ReleaseNotes(Sample, ReleaseVersion.Parse("1.2.0-rc.1"));

        using (Assert.Multiple())
        {
            await Assert.That(section).IsEqualTo("1.2.0-rc.1");
            await Assert.That(notes).IsEqualTo("- The release candidate.");
        }
    }

    [Test]
    public async Task PrereleaseWithoutASectionUsesUnreleased()
    {
        var (section, notes) = Changelog.ReleaseNotes(Sample, ReleaseVersion.Parse("1.3.0-rc.1"));

        using (Assert.Multiple())
        {
            await Assert.That(section).IsEqualTo(Changelog.Unreleased);
            await Assert.That(notes).IsEqualTo(Lines("### Changed", "", "- An unreleased change."));
        }
    }

    [Test]
    public async Task PrereleaseFailsWhenUnreleasedIsEmptyToo()
    {
        string[] lines = ["## [Unreleased]", "", "## [1.0.0]", "- First release."];

        await Assert.That(() => Changelog.ReleaseNotes(lines, ReleaseVersion.Parse("1.1.0-rc.1")))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("'## [1.1.0-rc.1]' or '## [Unreleased]'", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("1.3.0")]
    [Arguments("1.1.0")]
    public async Task StableReleaseNeedsItsOwnSection(string version)
    {
        // Unreleased has notes, but a stable release never falls back to it.
        await Assert.That(() => Changelog.ReleaseNotes(Sample, ReleaseVersion.Parse(version)))
            .Throws<InvalidOperationException>()
            .WithMessageContaining($"no '## [{version}]' section", StringComparison.Ordinal);
    }

    [Test]
    public async Task EveryVersionInTheRepositoryChangelogHasReleaseNotes()
    {
        var lines = await File.ReadAllLinesAsync(Path.Combine(Repository.Root, Changelog.FileName));
        var versions = lines
            .Where(line => line.StartsWith("## [", StringComparison.Ordinal))
            .Select(line => line[4..line.IndexOf(']', StringComparison.Ordinal)])
            .Where(name => name != Changelog.Unreleased)
            .ToList();

        await Assert.That(versions).IsNotEmpty();
        foreach (var version in versions)
        {
            var release = ReleaseVersion.Parse(version);
            var (section, notes) = Changelog.ReleaseNotes(lines, release);

            using (Assert.Multiple())
            {
                await Assert.That(section).IsEqualTo(release.Version);
                await Assert.That(notes).IsNotEmpty();
            }
        }
    }
}
