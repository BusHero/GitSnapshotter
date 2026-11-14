# Changelog

All notable changes to the `GitSnapshotter` library and the `GitSnapshotter.Tool` .NET tool are recorded here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Add each change under **Unreleased** as it is made. A release takes its GitHub release description from the section
for its version: before releasing, rename **Unreleased** to the version (`## [1.2.0] - 2026-12-01`) and start a new,
empty **Unreleased** section above it. A prerelease with no section of its own uses **Unreleased**.

## [Unreleased]

## [0.2.0-rc.1] - 2026-11-14

The first release on nuget.org.

### Added

- The `GitSnapshotter` library, for .NET 8 and later and .NET Framework 4.7.2 and later. It takes a snapshot of a
  repository and restores it later:
  - HEAD, local branches and their upstreams, remotes with their URLs and remote-tracking branches, tags
    (lightweight and annotated), and other references such as `refs/stash` and `refs/notes/*`.
  - Optionally the index and working tree: staged content, modified files and untracked files.
  - Snapshots save to and load from JSON, and two snapshots can be compared.
  - `GitRepository.Preserve` puts a repository back after a test, even when the test fails.
- The `gitsnapshotter` command-line tool, published as the `GitSnapshotter.Tool` .NET tool, with `snapshot`,
  `restore` and `diff` commands.
- Symbol packages (`.snupkg`) with SourceLink for the library.

[Unreleased]: https://github.com/BusHero/GitSnapshotter/compare/v0.2.0-rc.1...HEAD
[0.2.0-rc.1]: https://github.com/BusHero/GitSnapshotter/releases/tag/v0.2.0-rc.1
