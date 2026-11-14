<h1>
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/logo/wordmark-dark.svg">
    <img alt="GitSnapshotter" src="assets/logo/wordmark-light.svg" width="380">
  </picture>
</h1>


[![build](https://github.com/BusHero/GitSnapshotter/actions/workflows/build.yml/badge.svg?branch=trunk)](https://github.com/BusHero/GitSnapshotter/actions/workflows/build.yml?query=branch%3Atrunk)
[![codeql](https://github.com/BusHero/GitSnapshotter/actions/workflows/codeql.yml/badge.svg?branch=trunk)](https://github.com/BusHero/GitSnapshotter/actions/workflows/codeql.yml?query=branch%3Atrunk)
[![docs](https://github.com/BusHero/GitSnapshotter/actions/workflows/docs.yml/badge.svg?branch=trunk)](https://github.com/BusHero/GitSnapshotter/actions/workflows/docs.yml?query=branch%3Atrunk)
[![License: MIT](https://img.shields.io/github/license/BusHero/GitSnapshotter)](LICENSE)

A .NET library and command-line tool that snapshots a git repository's state (HEAD, branches, remotes, tags, other
refs, and optionally the index and working tree) and restores it later.

Documentation: https://bushero.github.io/GitSnapshotter/ (or see [the package README](Source/GitSnapshotter/docs/README.md)).

## Development

Requires the .NET 11 SDK (see `global.json`) and the .NET 8 SDK. The library targets .NET 8 and .NET Framework 4.7.2,
and the tests run on .NET 8 and .NET 11; the .NET 8 tests need the .NET 8 SDK's app host, which the runtime alone
doesn't include. The .NET Framework 4.7.2 tests run on Windows only, which CI covers in a separate job.

```sh
./build.sh                 # run the build pipeline: build, test, pack the library and the tool, publish the console app
dotnet test --solution Source/GitSnapshotter.slnx
```

The pipeline is written with [ModularPipelines](https://github.com/thomhurst/ModularPipelines) in
`pipeline/GitSnapshotter.Pipeline`. Each step is a module in `Modules/`; modules run in parallel as far as their
`[DependsOn]` attributes allow. Its tests are in `pipeline/GitSnapshotter.Pipeline.Tests`, which compiles the
pipeline's sources and is part of the solution, so `dotnet test` and the `Test` target run them. Settings come from
the `Build` configuration section, as environment variables (`Build__PublishDirectory=out`) or arguments
(`./build.sh --Build:PublishDirectory=out`):

| Setting | Default | Purpose |
|---|---|---|
| `Target` | `All` | Which modules run, see below |
| `Configuration` | `Release` | Build configuration |
| `PublishDirectory` | `publish` | Where the packages and the console app go |
| `DefaultBranch` | from git | Decides the `+trunk` / `+merge` version suffix |
| `NuGetApiKey` | none | Pushes the packages (library and tool) when set; otherwise the push is skipped |
| `NuGetSource` | GitHub Packages | Feed to push to |
| `Version` | none | Release version (`1.2.0`, `1.2.0-rc.1`); without it the packages are branch prereleases |
| `ReleaseDirectory` | `release` | Where a release build puts the GitHub release assets |
| `ReleaseNotesDirectory` | `release-notes` | Where `VerifyRelease` writes the release description |
| `ReleaseEnvironment` | `nuget` | The GitHub environment `VerifyRelease` requires a reviewer on |

The CI workflows run the pipeline too, one target per job, so a job only sets up the machine and hands files to
the next. Each target runs on a developer machine as well (`./build.sh --Build:Target=Test`):

| Target | Modules | Used by |
|---|---|---|
| `All` | Format check, compile, tests, third-party notices, console app, packages, push; on a release, the release assets (packages, console app zip, `SHA256SUMS.txt`) | `build` > ubuntu-latest |
| `VerifyRelease` | Checks the release may go ahead and writes its description from `CHANGELOG.md`; needs `GITHUB_TOKEN` | `build` > verify |
| `Test` | Compile and tests | `build` > windows-latest |
| `Compile` | Compile | `codeql` |
| `Restore` | Restore the solution | `build` > dependency-submission |
| `Docs` | `npm ci` and `npm run build` in `docs` | `docs` |

On a build server the pipeline also gives git a user name and email for the tests; on a developer machine it leaves
the git configuration alone.

The pipeline also generates `THIRD-PARTY-NOTICES.txt` from each project's restore graph: one next to the console app
in the publish directory and inside the tool package, covering every package the app ships, and one inside the
library package, covering the packages compiled into the library (Polyfill). License texts come from the packages themselves; a package whose
license has no text there (an expression other than MIT, or none) fails the build until its text is added to
`pipeline/GitSnapshotter.Pipeline/ThirdPartyNotices.cs`.

Package versions live in `Directory.Packages.props`, and each project's resolved package graph is recorded in its
`packages.lock.json`. A normal restore updates the lock files; commit them with the version change. CI (and the
pre-push hook) restore in locked mode and fail if a lock file is out of date.

Every push to `trunk` publishes prerelease packages, so enable the pre-push hook once per clone. It runs the
test suite on the commit being pushed to `trunk` and stops the push if anything fails:

```sh
git config core.hooksPath .githooks
```

`git push --no-verify` skips it.

| Project | Purpose |
|---|---|
| `Source/GitSnapshotter` | The library, published as the `GitSnapshotter` NuGet package |
| `Source/GitSnapshotter.Console` | The command-line tool, published as the `GitSnapshotter.Tool` .NET tool (`gitsnapshotter`) |
| `Source/GitSnapshotter.UnitTests` | [TUnit](https://tunit.dev) tests; each test builds throwaway repositories in the temp directory |
| `pipeline/GitSnapshotter.Pipeline` | Build pipeline (ModularPipelines) |
| `docs` | Documentation site ([Astro](https://astro.build) + [Starlight](https://starlight.astro.build)) |

### Releasing

Releases come from the same `build` workflow as CI builds. Start it by hand: **Actions > build > Run workflow**, on
`trunk`, with the version to release (`1.2.0`, or `1.2.0-rc.1` for a prerelease). With no version, the run is an
ordinary CI build. A release builds the latest commit on `trunk` and creates the `v1.2.0` tag when the release is
published. From a terminal:

```sh
gh workflow run build.yml --ref trunk -f version=1.2.0
```

The release description comes from [`CHANGELOG.md`](CHANGELOG.md). Record each change under **Unreleased** as it is
made. Before a stable release, rename **Unreleased** to the version (`## [1.2.0] - 2026-12-01`) and start a new,
empty **Unreleased** above it; the release fails if the version has no section. A prerelease uses its own section if
there is one, otherwise **Unreleased**.

The workflow (`.github/workflows/build.yml`) never publishes without approval:

1. **verify** checks that the version is valid and has no tag yet, that the run is on the default branch, that
   `CHANGELOG.md` describes it, and that the `nuget` environment requires a reviewer. If anyone removes that protection, releases fail instead of
   publishing unattended.
2. **windows-latest** runs the tests on Windows.
3. **ubuntu-latest** runs the pipeline with `Build:Version` set to the version. On a release it gets no token, so the
   code being built cannot publish anything.
4. **publish** waits for a reviewer to approve the `nuget` environment. It then creates a draft GitHub release with
   both NuGet packages (the `GitSnapshotter` library and the `GitSnapshotter.Tool` .NET tool), the zipped console
   app and `SHA256SUMS.txt`, pushes the packages to nuget.org, and publishes the release, which creates the tag. It
   is the only job that can write to the repository or get a nuget.org key.

A version with a prerelease label (`1.2.0-rc.1`) makes a GitHub prerelease and a NuGet prerelease. The release stays
a draft until the push to nuget.org succeeds, so a failed run can be re-run.

Setup, once:

- Create a `nuget` environment (Settings > Environments) with at least one required reviewer.
- Create a trusted publishing policy on nuget.org (account > Trusted Publishing) with repository owner `BusHero`,
  repository `GitSnapshotter`, workflow file `build.yml` and environment `nuget`. The push uses
  [trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing), so there is no API key to
  store or rotate: the `NuGet/login` action exchanges the workflow's OIDC token for a short-lived key.
- Optionally, add a tag ruleset (Settings > Rules) so only maintainers can create `v*` tags. Add GitHub Actions
  to its bypass list: the workflow creates the tag when it publishes the release.

## Documentation site

The site in `docs/` is deployed to GitHub Pages by `.github/workflows/docs.yml` on every push to `trunk` that
touches it. To work on it locally (Node.js 22.23.4 or later):

```sh
cd docs
npm install
npm run dev                # http://localhost:4321/GitSnapshotter/
```

Pages live in `docs/src/content/docs/` as Markdown.
