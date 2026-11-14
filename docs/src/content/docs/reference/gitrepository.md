---
title: GitRepository
description: The static class that takes, restores and preserves snapshots.
---

```csharp
namespace GitSnapshotter;

public static class GitRepository
```

The entry point of the library. All methods are thread-safe in the sense that they share no state, but two calls must
not change the same repository at the same time.

## GetSnapshot

```csharp
public static GitRepositorySnapshot GetSnapshot(string pathToRepository, SnapshotOptions? options = null);
public static GitRepositorySnapshot GetSnapshot(IRepository repository, SnapshotOptions? options = null);
```

Takes a snapshot of the repository at `pathToRepository`, or of an open LibGit2Sharp repository.

| Parameter | Description |
|---|---|
| `pathToRepository` | The repository's working directory |
| `repository` | An open `LibGit2Sharp.IRepository`. It isn't disposed. |
| `options` | What to record. `null` means [`SnapshotOptions.Default`](/GitSnapshotter/reference/options/#snapshotoptions): references only. |

**Returns** a [`GitRepositorySnapshot`](/GitSnapshotter/reference/snapshot-types/#gitrepositorysnapshot).

**Throws**

- `LibGit2Sharp.RepositoryNotFoundException`: the path isn't a git repository.
- `InvalidOperationException`: `IncludeWorkingTree` is set and the index has unresolved merge conflicts.

Changes no references or files. With `IncludeWorkingTree`, writes blob and tree objects to the object database.

See [Taking snapshots](/GitSnapshotter/library/taking-snapshots/).

## Restore

```csharp
public static void Restore(string pathToRepository, GitRepositorySnapshot snapshot, RestoreOptions? options = null);
public static void Restore(IRepository repository, GitRepositorySnapshot snapshot, RestoreOptions? options = null);
```

Restores the repository to `snapshot`.

| Parameter | Description |
|---|---|
| `pathToRepository` | The repository's working directory |
| `repository` | An open `LibGit2Sharp.IRepository`. It isn't disposed. |
| `snapshot` | The snapshot to restore |
| `options` | What to restore. `null` means [`RestoreOptions.Default`](/GitSnapshotter/reference/options/#restoreoptions): everything, without discarding uncommitted changes. |

**Throws**

- [`MissingObjectsException`](/GitSnapshotter/reference/exceptions/#missingobjectsexception): the snapshot needs
  objects the repository doesn't have. Nothing was changed.
- [`UncommittedChangesException`](/GitSnapshotter/reference/exceptions/#uncommittedchangesexception): the restore
  would overwrite uncommitted changes and `Force` is off. Nothing was changed.
- `LibGit2Sharp.RepositoryNotFoundException`: the path isn't a git repository.

See [Restoring](/GitSnapshotter/library/restoring/).

## Preserve

```csharp
public static SnapshotScope Preserve(
    string pathToRepository,
    SnapshotOptions? snapshotOptions = null,
    RestoreOptions? restoreOptions = null);
```

Takes a snapshot now and returns a [`SnapshotScope`](#snapshotscope) that restores it when disposed.

| Parameter | Description |
|---|---|
| `pathToRepository` | The repository's working directory |
| `snapshotOptions` | Passed to `GetSnapshot` |
| `restoreOptions` | Passed to `Restore` when the scope is disposed |

```csharp
using (GitRepository.Preserve(path))
{
    // change branches, tags, commits...
} // the repository is restored here
```

See [Testing](/GitSnapshotter/library/testing/).

## SnapshotScope

```csharp
public sealed class SnapshotScope : IDisposable
```

Returned by `Preserve`; it has no public constructor.

| Member | Description |
|---|---|
| `GitRepositorySnapshot Snapshot { get; }` | The snapshot that will be restored |
| `void Dispose()` | Restores the snapshot. Later calls do nothing, even if the first one threw. |

`Dispose` throws whatever `Restore` throws.
