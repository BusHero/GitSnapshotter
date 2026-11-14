---
title: Choosing what to restore
description: Restore only some parts of a snapshot with RestoreOptions.
---

By default `Restore` puts back everything the snapshot holds. `RestoreOptions` has a switch for each part, so you
can leave the others as they are now.

```csharp
GitRepository.Restore(path, snapshot, new RestoreOptions
{
    Head = true,          // the checked-out branch or detached commit
    Branches = true,      // local branches, their tips and their upstream
    Remotes = true,       // remotes, their URLs and remote-tracking branches
    Tags = true,
    References = true,    // refs/stash, notes and other references
    WorkingTree = true,   // only applies when the snapshot has one
    Force = false,        // allow discarding uncommitted changes
});
```

All switches default to `true` except `Force`. `RestoreOptions` is a record, so start from the defaults and turn
off only what you need:

```csharp
var keepTags = RestoreOptions.Default with { Tags = false };
```

## Recipes

### Leave tags alone

Useful when tags are created by something you want to keep, such as a release that happened in the meantime:

```csharp
GitRepository.Restore(path, snapshot, new RestoreOptions { Tags = false });
```

### Restore branches but stay where you are

`Head = false` keeps the current branch checked out. Its tip still moves if it is one of the restored branches:

```csharp
GitRepository.Restore(path, snapshot, new RestoreOptions { Head = false });
```

If the current branch didn't exist when the snapshot was taken, it is deleted, and HEAD is left detached at the
commit it pointed to.

### Only go back to the original branch

To switch back to the branch you were on without touching any refs:

```csharp
GitRepository.Restore(path, snapshot, new RestoreOptions
{
    Branches = false,
    Remotes = false,
    Tags = false,
    References = false,
});
```

HEAD is attached to that branch at its *current* tip. If the branch has been deleted since, HEAD ends up on an
unborn branch of that name and the index and working tree are emptied, so check that it still exists first.

### Keep the stash

`refs/stash` is one of the "other references". To keep whatever you stashed since the snapshot:

```csharp
GitRepository.Restore(path, snapshot, new RestoreOptions { References = false });
```

### Restore references but not the working tree

A snapshot taken with `IncludeWorkingTree` also restores uncommitted work. To restore only its references:

```csharp
GitRepository.Restore(path, snapshot, new RestoreOptions { WorkingTree = false });
```

The working tree is then handled as for a snapshot without one: reset if HEAD's commit changes, otherwise left
alone.

## Safety checks follow the options

The checks described in [Restoring](/GitSnapshotter/library/restoring/#nothing-is-changed-if-the-restore-cant-finish)
only look at the parts you restore. A snapshot whose tags point at commits that no longer exist can still be
restored with `Tags = false`.
