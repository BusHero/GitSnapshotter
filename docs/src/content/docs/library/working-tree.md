---
title: Working tree snapshots
description: Capture and restore staged changes, modified files and untracked files.
---

A plain snapshot records references only. With `IncludeWorkingTree`, it also records uncommitted work:

```csharp
var snapshot = GitRepository.GetSnapshot(path, new SnapshotOptions { IncludeWorkingTree = true });
```

## What is captured

| Captured | Not captured |
|---|---|
| Staged content (the index), separately from the working directory | Ignored files |
| Modified tracked files | Contents of submodules and nested repositories |
| Deleted tracked files | Unresolved merge conflicts (capturing fails, see below) |
| Untracked, non-ignored files, in any folder | Empty folders (git doesn't track folders) |
| The executable bit, on Linux and macOS | |
| Symbolic links, on .NET 8 and later | |

The index and the working directory are captured separately. A file that is staged with one content and modified
again afterwards comes back the same way: the staged version in the index, the newer version on disk.

## How it is stored

Capturing writes the content as git objects: blobs for the files, and two tree objects, one for the index and one
for the working directory. The snapshot only stores the two tree ids, in `WorkingTree.IndexTree` and
`WorkingTree.WorkingDirectoryTree`. No references, files or the index are changed, so taking the snapshot is
invisible to `git status`.

:::caution[The objects can be garbage-collected]
The trees aren't referenced by any branch, tag or reflog. `git gc` prunes unreferenced objects once they are older
than `gc.pruneExpire`, two weeks by default, and git runs `gc --auto` by itself from time to time. A working tree
snapshot is meant for minutes or days, not months. Restoring after the objects are gone throws
`MissingObjectsException`.
:::

## Restoring it

When a snapshot has a working tree and `RestoreOptions.WorkingTree` is on (the default), the restore:

1. resets the index and working tree to the restored HEAD commit,
2. deletes untracked files that weren't in the snapshot,
3. writes back modified, deleted and untracked files from the snapshot, and
4. replaces the index with the recorded one.

Ignored files are never touched, so build output and local settings survive.

Because step 2 deletes files, the safety check is stricter than for a plain restore: an untracked file that isn't in
the snapshot blocks the restore with `UncommittedChangesException` unless `Force` is set. Files whose content
already matches the snapshot don't count.

## Merge conflicts

The index can't be written as a tree while it has unresolved conflicts. Taking a working tree snapshot in the middle
of a conflicted merge, rebase or cherry-pick throws `InvalidOperationException`. Resolve or abort it first, or
take a references-only snapshot.

## Comparing

`SnapshotDiff.Compare` reports a `WorkingTree` change when the index or working directory trees differ, but only
when **both** snapshots include the working tree; a snapshot without one says nothing about it. See
[Comparing snapshots](/GitSnapshotter/library/comparing-snapshots/).
