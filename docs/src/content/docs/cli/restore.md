---
title: restore
description: Restore a repository from a snapshot file, or preview what would change.
---

```text
gitsnapshotter restore <repository> <snapshot-file> [--force] [--dry-run]
```

Puts `<repository>` back to the state recorded in `<snapshot-file>`: HEAD, branches and their upstreams, remotes,
tags and other references, and the working tree if the snapshot has one. Branches, tags and remotes created since the
snapshot are deleted.

## Options

| Option | Description |
|---|---|
| `--dry-run` | Don't change anything; list what the restore would change. |
| `--force` | Discard uncommitted changes that the restore would otherwise refuse to overwrite. |

## Examples

Preview, then restore:

```console
$ gitsnapshotter restore . ../before.json --dry-run
~ Head HEAD experiment -> main
~ Branch feature 7bb0b8d12decfd8d9f6deb827c519008c80dcf4c -> b803aa0e577d422308d789b5a64ec784def50469
- Branch experiment 7bb0b8d12decfd8d9f6deb827c519008c80dcf4c
+ Tag v1.0 b803aa0e577d422308d789b5a64ec784def50469 (annotation 2fd9fb7f8beecc53171b02184a760937711b3b70)

$ gitsnapshotter restore . ../before.json
Restored . from ../before.json.
```

The dry run lists changes from the repository's point of view: `-` means the restore will delete it, `+` that it
will create it. If there is nothing to do, it prints `Nothing to restore.`

When uncommitted changes are in the way:

```console
$ gitsnapshotter restore . ../before.json
error: The working tree has uncommitted changes that the restore would overwrite: src/app.cs. Commit or stash them, or restore with Force.

$ git stash                                     # keep them
$ gitsnapshotter restore . ../before.json

$ gitsnapshotter restore . ../before.json --force   # or throw them away
```

## What gets checked

Before changing anything, `restore` checks that every commit the snapshot needs still exists and that no
uncommitted work would be lost. If either check fails it exits with code `1` and the repository is untouched.

A dirty working tree only matters if the restore would change those files: if HEAD's commit stays the same, you can
restore branches, tags and remotes with uncommitted changes around.

See [Restoring](/GitSnapshotter/library/restoring/) for exactly what is restored and how, and
[How it works](/GitSnapshotter/concepts/how-it-works/) for the order of operations.

## Notes

- `--dry-run` doesn't check for uncommitted changes. A dry run can succeed for a restore that would then refuse to
  run.
- `--dry-run` and `--force` together just preview.
- The tool always restores every part of the snapshot. To restore only some parts, use the library's
  [`RestoreOptions`](/GitSnapshotter/library/choosing-what-to-restore/).
