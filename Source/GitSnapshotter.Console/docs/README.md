# GitSnapshotter.Tool

`gitsnapshotter` takes a snapshot of a git repository's references (HEAD, branches, remotes, tags, other refs and,
optionally, the index and working tree) and later puts the repository back exactly as it was.

Requires the .NET 11 runtime or later.

## Install

```sh
dotnet tool install --global GitSnapshotter.Tool
```

## Usage

```text
gitsnapshotter snapshot <repository> [--output <file>] [--include-working-tree]
    Take a snapshot. Prints JSON, or writes it to <file>.
gitsnapshotter restore <repository> <snapshot-file> [--force] [--dry-run]
    Restore a snapshot. --force overwrites uncommitted changes;
    --dry-run only lists what would change.
gitsnapshotter diff <repository> <snapshot-file>
    List what changed in the repository since the snapshot was taken.
gitsnapshotter help
    Show this message.
```

For example:

```sh
gitsnapshotter snapshot . --output ../before.json
# ... create branches, commit, move tags ...
gitsnapshotter diff . ../before.json
gitsnapshotter restore . ../before.json
```

Exit codes: `0` success, `1` failure, `2` usage error.

Documentation: https://bushero.github.io/GitSnapshotter/

The library behind the tool is the [GitSnapshotter](https://www.nuget.org/packages/GitSnapshotter) package.
