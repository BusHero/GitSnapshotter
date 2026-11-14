---
title: Restoring
description: What Restore checks, what it changes, and how to restore only part of a snapshot.
sidebar:
  order: 2
---

```csharp
GitRepository.Restore(path, snapshot);
```

## Checks before anything changes

`Restore` checks everything before changing anything:

- If the snapshot refers to objects the repository no longer has, it throws `MissingObjectsException`. Its
  `MissingObjects` property lists the ids.
- If restoring would overwrite uncommitted changes, it throws `UncommittedChangesException` and lists the paths in
  `Paths`. This works like `git checkout`: only changes that would actually be lost count. Set
  `RestoreOptions.Force` to discard them.

In both cases the repository is left untouched. Both exceptions derive from `GitSnapshotterException`.

```csharp
try
{
    GitRepository.Restore(path, snapshot);
}
catch (UncommittedChangesException e)
{
    Console.WriteLine($"Commit or stash these first: {string.Join(", ", e.Paths)}");
}
```

## What happens to the working tree

When the checked-out branch's commit changes, the index and working tree are updated to match (a hard reset).
Untracked files are kept unless the snapshot includes the working tree.

If the snapshot was taken with `IncludeWorkingTree`, the index and working directory are put back as they were,
and untracked files that weren't there are deleted. Ignored files are never touched.

## Choosing what to restore

Every part is restored by default. Turn off the ones you want left alone:

```csharp
GitRepository.Restore(path, snapshot, new RestoreOptions
{
    Head = true,
    Branches = true,      // tips and upstream configuration
    Remotes = true,
    Tags = false,         // leave tags as they are
    References = true,
    WorkingTree = true,   // only applies when the snapshot has one
    Force = false,
});
```

## Preserve: restore on dispose

`GitRepository.Preserve` takes a snapshot and returns a `SnapshotScope` that restores it once, when disposed.
It accepts both option types:

```csharp
using var scope = GitRepository.Preserve(
    path,
    new SnapshotOptions { IncludeWorkingTree = true },
    new RestoreOptions { Force = true });

// scope.Snapshot is the snapshot that will be restored
```

This is handy in tests: the repository is restored even if the test throws.
