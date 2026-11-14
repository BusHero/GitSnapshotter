---
title: Command-line overview
description: How gitsnapshotter is invoked, what it prints, and its exit codes.
sidebar:
  label: Overview
---

`gitsnapshotter` brings the library's three operations to the shell: take a snapshot, restore one, and show
what changed. See [Installation](/GitSnapshotter/installation/#the-command-line-tool) for how to get it.

```text
gitsnapshotter snapshot <repository> [--output <file>] [--include-working-tree]
gitsnapshotter restore  <repository> <snapshot-file> [--force] [--dry-run]
gitsnapshotter diff     <repository> <snapshot-file>
gitsnapshotter help
```

| Command | Does |
|---|---|
| [`snapshot`](/GitSnapshotter/cli/snapshot/) | Takes a snapshot and prints it as JSON, or writes it to a file |
| [`restore`](/GitSnapshotter/cli/restore/) | Restores a snapshot file, or previews the restore with `--dry-run` |
| [`diff`](/GitSnapshotter/cli/diff/) | Lists what changed in the repository since a snapshot was taken |
| `help` | Prints usage. `--help` and `-h` do the same |

## Arguments

- `<repository>` is the repository's working directory, the folder that contains `.git`. Use `.` for the current
  folder.
- `<snapshot-file>` is a file written by `snapshot --output`, or by the library's `Save`. The file format is the
  same, so the library and the tool can read each other's snapshots.
- Options can come before or after the positional arguments. Unknown options are an error.

## Output

Results go to standard output; errors go to standard error, prefixed with `error:`. A usage error also prints the
usage text.

```console
$ gitsnapshotter restore . before.json
error: The working tree has uncommitted changes that the restore would overwrite: src/app.cs. Commit or stash them, or restore with Force.
```

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | The operation failed: uncommitted changes, missing objects, not a git repository, a file that can't be read or isn't a snapshot, or merge conflicts when capturing the working tree. For uncommitted changes and missing objects the repository is untouched. |
| `2` | Bad arguments: unknown command or option, a missing value, or the wrong number of arguments |

`diff` and `restore --dry-run` exit with `0` whether or not there are changes. See
[Scripting](/GitSnapshotter/cli/scripting/#detecting-changes) for how to test for changes.

## Compared with the library

The tool covers the common cases. For these, use the [library](/GitSnapshotter/library/taking-snapshots/):

- Restoring only some parts of a snapshot (`RestoreOptions`); the tool always restores everything the snapshot has.
- Working with the list of changes as data rather than text.
