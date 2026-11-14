# GitSnapshotter

A .NET library and command-line tool that snapshots a git repository's state (HEAD, branches, remotes, tags, other
refs, and optionally the index and working tree) and restores it later.

See [the package README](Source/GitSnapshotter/docs/README.md) for usage.

## Development

Requires the .NET 10 SDK (see `global.json`).

```sh
./build.sh Compile         # compile and run the tests (NUKE)
dotnet test Source/GitSnapshotter.sln
```

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
| `build/` | NUKE build: Compile, RunUnitTests, Pack, PublishPackage |
