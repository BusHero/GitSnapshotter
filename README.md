# GitSnapshotter

A .NET library and command-line tool that snapshots a git repository's state (HEAD, branches, remotes, tags, other
refs, and optionally the index and working tree) and restores it later.

See [the package README](Source/GitSnapshotter/docs/README.md) for usage.

## Development

Requires the .NET 10 SDK (see `global.json`).

```sh
./build.sh                 # run the build pipeline: build, test, pack, publish the console app
dotnet test Source/GitSnapshotter.slnx
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
| `Source/GitSnapshotter.UnitTests` | xUnit tests; each test builds throwaway repositories in the temp directory |
| `pipeline/GitSnapshotter.Pipeline` | Build pipeline (ModularPipelines) |
