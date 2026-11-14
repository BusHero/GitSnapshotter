---
title: Command line
description: Use GitSnapshotter.Console to snapshot, restore and diff from a shell.
sidebar:
  order: 4
---

The `GitSnapshotter.Console` tool wraps the library. Builds are attached to each CI run as the
`GitSnapshotter.Console_<version>` artifact, or build it yourself:

```sh
./build.sh        # the console app lands in ./publish
```

## Commands

```text
GitSnapshotter.Console snapshot <repository> [--output <file>] [--include-working-tree]
GitSnapshotter.Console restore <repository> <snapshot-file> [--force] [--dry-run]
GitSnapshotter.Console diff <repository> <snapshot-file>
GitSnapshotter.Console help
```

### snapshot

Prints the snapshot as JSON, or writes it to a file with `--output` (`-o`). `--include-working-tree` also records
the index and working directory.

```sh
GitSnapshotter.Console snapshot . -o before.json
```

### restore

Restores a snapshot file. `--force` overwrites uncommitted changes. `--dry-run` only lists what would change.

```sh
GitSnapshotter.Console restore . before.json --dry-run
GitSnapshotter.Console restore . before.json
```

### diff

Lists what changed in the repository since the snapshot was taken.

```sh
GitSnapshotter.Console diff . before.json
```

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Error, for example uncommitted changes or missing objects |
| `2` | Bad arguments |
