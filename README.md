# GitSnapshotter

A .NET library and command-line tool that snapshots a git repository's state (HEAD, branches, remotes, tags, other
refs, and optionally the index and working tree) and restores it later.

See [the package README](Source/GitSnapshotter/docs/README.md) for usage.

## Development

```sh
./build.sh Compile         # compile and run the tests (NUKE)
dotnet test Source/GitSnapshotter.sln
```

| Project | Purpose |
|---|---|
| `Source/GitSnapshotter` | The library, published as the `GitSnapshotter` NuGet package |
| `Source/GitSnapshotter.Console` | The command-line tool |
| `Source/GitSnapshotter.UnitTests` | xUnit tests; each test builds throwaway repositories in the temp directory |
| `build/` | NUKE build: Compile, RunUnitTests, Pack, PublishPackage |
