# Changelog

All notable changes to the `GitSnapshotter` library and the `GitSnapshotter.Tool` .NET tool are recorded here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Add each change under **Unreleased** as it is made. A release takes its GitHub release description from the section
for its version: before releasing, rename **Unreleased** to the version (`## [1.2.0] - 2026-12-01`) and start a new,
empty **Unreleased** section above it. A prerelease with no section of its own uses **Unreleased**.

## [Unreleased]

### Changed

- `gitsnapshotter` parses its command line with System.CommandLine. Each command has its own help
  (`gitsnapshotter help restore` or `gitsnapshotter restore --help`), and the tool now accepts `--version`.
  Error messages for a wrong command line are worded differently; the exit codes are unchanged.

## [0.2.0] - 2026-11-14

The first stable release made by the release workflow, with the library and the tool at the same version. The code
is the same as in 0.2.0-rc.2. 0.1.0 was published without a changelog, so this lists what the packages contain.

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
- Symbol packages (`.snupkg`) with SourceLink for both packages.

## [0.2.0-rc.2] - 2026-11-14

Publishes the `GitSnapshotter` library alongside the tool, which 0.2.0-rc.1 left out. The code of both is the same as
in 0.2.0-rc.1.

### Fixed

- Releases pack the library and the tool at the release version. 0.2.0-rc.1 packed the library as 0.1.0, so only
  `GitSnapshotter.Tool` was published at 0.2.0-rc.1.

## [0.2.0-rc.1] - 2026-11-14

The first release made by the release workflow, and the first with a changelog. Only `GitSnapshotter.Tool` was
published at this version: a packaging error built the library as 0.1.0, which was already on nuget.org, so there is
no `GitSnapshotter` 0.2.0-rc.1 package.

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

[Unreleased]: https://github.com/BusHero/GitSnapshotter/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/BusHero/GitSnapshotter/compare/v0.2.0-rc.2...v0.2.0
[0.2.0-rc.2]: https://github.com/BusHero/GitSnapshotter/compare/v0.2.0-rc.1...v0.2.0-rc.2
[0.2.0-rc.1]: https://github.com/BusHero/GitSnapshotter/releases/tag/v0.2.0-rc.1
