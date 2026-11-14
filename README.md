<h1>
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/logo/wordmark-dark.svg">
    <img alt="GitSnapshotter" src="assets/logo/wordmark-light.svg" width="380">
  </picture>
</h1>

A .NET library and command-line tool that snapshots a git repository's state (HEAD, branches, remotes, tags, other
refs, and optionally the index and working tree) and restores it later.

Documentation: https://bushero.github.io/GitSnapshotter/ (or see [the package README](Source/GitSnapshotter/docs/README.md)).

## Development

Requires the .NET 11 SDK (see `global.json`) and the .NET 8 SDK. The library targets .NET 8 and .NET Framework 4.7.2,
and the tests run on .NET 8 and .NET 11; the .NET 8 tests need the .NET 8 SDK's app host, which the runtime alone
doesn't include. The .NET Framework 4.7.2 tests run on Windows only, which CI covers in a separate job.

```sh
./build.sh                 # run the build pipeline: build, test, pack, publish the console app
dotnet test --solution Source/GitSnapshotter.slnx
```

The pipeline is written with [ModularPipelines](https://github.com/thomhurst/ModularPipelines) in
`pipeline/GitSnapshotter.Pipeline`. Each step is a module in `Modules/`; modules run in parallel as far as their
`[DependsOn]` attributes allow. Settings come from the `Build` configuration section, as environment variables
(`Build__PublishDirectory=out`) or arguments (`./build.sh --Build:PublishDirectory=out`):

| Setting | Default | Purpose |
|---|---|---|
| `Configuration` | `Release` | Build configuration |
| `PublishDirectory` | `publish` | Where the package and the console app go |
| `DefaultBranch` | from git | Decides the `+trunk` / `+merge` version suffix |
| `NuGetApiKey` | none | Pushes the package when set; otherwise the push is skipped |
| `NuGetSource` | GitHub Packages | Feed to push to |

The pipeline also generates `THIRD-PARTY-NOTICES.txt` from each project's restore graph: one next to the console app
in the publish directory, covering every package the app ships, and one inside the NuGet package, covering the
packages compiled into the library (Polyfill). License texts come from the packages themselves; a package whose
license has no text there (an expression other than MIT, or none) fails the build until its text is added to
`pipeline/GitSnapshotter.Pipeline/ThirdPartyNotices.cs`.

Package versions live in `Directory.Packages.props`, and each project's resolved package graph is recorded in its
`packages.lock.json`. A normal restore updates the lock files; commit them with the version change. CI (and the
pre-push hook) restore in locked mode and fail if a lock file is out of date.

Every push to `trunk` publishes a prerelease package, so enable the pre-push hook once per clone. It runs the
test suite on the commit being pushed to `trunk` and stops the push if anything fails:

```sh
git config core.hooksPath .githooks
```

`git push --no-verify` skips it.

| Project | Purpose |
|---|---|
| `Source/GitSnapshotter` | The library, published as the `GitSnapshotter` NuGet package |
| `Source/GitSnapshotter.Console` | The command-line tool |
| `Source/GitSnapshotter.UnitTests` | [TUnit](https://tunit.dev) tests; each test builds throwaway repositories in the temp directory |
| `pipeline/GitSnapshotter.Pipeline` | Build pipeline (ModularPipelines) |
| `docs` | Documentation site ([Astro](https://astro.build) + [Starlight](https://starlight.astro.build)) |

## Documentation site

The site in `docs/` is deployed to GitHub Pages by `.github/workflows/docs.yml` on every push to `trunk` that
touches it. To work on it locally (Node.js 22.23.4 or later):

```sh
cd docs
npm install
npm run dev                # http://localhost:4321/GitSnapshotter/
```

Pages live in `docs/src/content/docs/` as Markdown.
