---
title: API reference
description: The public types in the GitSnapshotter package.
---

All types live in the `GitSnapshotter` namespace.

## GitRepository

Static entry point. Every method has an overload taking a path and one taking an open `LibGit2Sharp.IRepository`.

| Method | Description |
|---|---|
| `GetSnapshot(path, SnapshotOptions? options = null)` | Records the repository's current state. |
| `Restore(path, GitRepositorySnapshot snapshot, RestoreOptions? options = null)` | Puts the repository back to `snapshot`. Throws `MissingObjectsException` or `UncommittedChangesException` without changing anything. |
| `Preserve(path, SnapshotOptions? snapshotOptions = null, RestoreOptions? restoreOptions = null)` | Takes a snapshot and returns a `SnapshotScope` that restores it on dispose. Path overload only. |

## SnapshotOptions

| Property | Default | Description |
|---|---|---|
| `IncludeWorkingTree` | `false` | Also record the index and working directory (tracked and untracked, non-ignored files). |

`SnapshotOptions.Default` is the default instance.

## RestoreOptions

| Property | Default | Description |
|---|---|---|
| `Head` | `true` | Restore HEAD (the checked-out branch, or the detached commit). |
| `Branches` | `true` | Restore local branches, their tips and upstream configuration. |
| `Remotes` | `true` | Restore remotes, their URLs and remote-tracking branches. |
| `Tags` | `true` | Restore tags. |
| `References` | `true` | Restore other references such as `refs/stash` and notes. |
| `WorkingTree` | `true` | Restore the index and working tree when the snapshot contains them. |
| `Force` | `false` | Allow overwriting uncommitted changes. |

`RestoreOptions.Default` is the default instance.

## GitRepositorySnapshot

| Member | Description |
|---|---|
| `Head` | The checked-out branch name, or the commit id when detached. |
| `IsHeadDetached` | `true` when HEAD pointed directly at a commit. |
| `Branches` | `Branch[]`: local branches. |
| `Remotes` | `Remote[]`: configured remotes and their remote-tracking branches. |
| `Tags` | `Tag[]`: lightweight and annotated tags. |
| `References` | Other direct references, keyed by canonical name (e.g. `refs/stash`). |
| `WorkingTree` | `WorkingTreeState?`: present when taken with `IncludeWorkingTree`. |
| `ToJson()` / `FromJson(string)` | Serialize to and from indented JSON. |
| `Save(string path)` / `Load(string path)` | Write to and read from a file. |

### Branch

`Name`, `Tip` (commit id), and the optional upstream: `TrackedBranch` (e.g. `origin/main`), `RemoteName`
(`branch.<name>.remote`) and `UpstreamBranch` (`branch.<name>.merge`, e.g. `refs/heads/main`).

### Remote

`Name`, `Url` (fetch URL), `Branches` (remote-tracking branches keyed by short name, with their commit ids) and
`Head` (the branch `refs/remotes/<remote>/HEAD` points at, if set).

### Tag

`Name`, `Target` (the object the tag points at), and for annotated tags `Message` and `AnnotationSha`. When the
annotation object is still in the database, restore reuses it so the tag comes back unchanged.

### WorkingTreeState

`IndexTree` (tree of the staged content) and `WorkingDirectoryTree` (tree of tracked and untracked, non-ignored
files).

## SnapshotScope

Returned by `Preserve`. `Snapshot` is the snapshot that will be restored; `Dispose()` restores it once, later calls
do nothing.

## SnapshotDiff

| Member | Description |
|---|---|
| `Compare(before, after)` | Lists what changed going from `before` to `after`. |
| `Changes` | `IReadOnlyList<SnapshotChange>`, grouped by category. |
| `HasChanges` | `true` when the snapshots differ. |

`SnapshotChange(Kind, Category, Name, Before, After)` is one difference; see
[Comparing snapshots](/GitSnapshotter/guides/diff/).

## Exceptions

| Type | Thrown when | Extra data |
|---|---|---|
| `GitSnapshotterException` | Base class for the errors below. | |
| `MissingObjectsException` | The snapshot refers to objects missing from the repository. | `MissingObjects` |
| `UncommittedChangesException` | A restore would overwrite uncommitted changes and `Force` is off. | `Paths` |

In both cases nothing was changed.
