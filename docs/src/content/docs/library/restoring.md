---
title: Restoring
description: Put a repository back to a snapshot, and what that changes.
---

```csharp
GitRepository.Restore(path, snapshot);
```

After `Restore` returns, the repository's references match the snapshot exactly. Anything that exists now but
didn't then is removed, anything that was deleted is recreated, and anything that moved is moved back.

## What a restore changes

| Part | Effect |
|---|---|
| Branches | Branches that weren't in the snapshot are **deleted**, including the one checked out. Missing branches are recreated and moved branches are reset to their recorded tip. |
| Upstreams | `branch.<name>.remote` and `branch.<name>.merge` are set back, or removed if the branch had no upstream. |
| Remotes | Remotes that weren't in the snapshot are removed. Missing remotes are added back with their URL; changed URLs are set back. Remote-tracking branches and `refs/remotes/<remote>/HEAD` are restored too. |
| Tags | Extra tags are deleted, missing tags recreated, and moved tags moved back. Lightweight and annotated tags keep their kind. |
| Other references | `refs/stash`, notes and any other references are recreated, moved back or deleted. |
| HEAD | The recorded branch is checked out again, or HEAD is detached at the recorded commit. |
| Index and working tree | Reset to the new HEAD commit, if HEAD's commit changed. If the snapshot includes the working tree, uncommitted work is put back instead. |

Things that didn't change are left alone, so restoring the same snapshot twice does nothing the second time.

### Annotated tags

When a tag is restored, GitSnapshotter reuses the original annotation object if it is still in the object database,
so the tag comes back byte for byte, with the same id, tagger and date. Deleting a tag with `git tag -d` leaves the
annotation object behind, so this is the usual case.

If the annotation has been garbage-collected, a new annotation is created with the same name, target and message.
Its tagger is taken from your git configuration (`user.name` and `user.email`), or `GitSnapshotter
<gitsnapshotter@localhost>` if none is set, and it is dated now, so its id differs from the original.

### The working tree

When the restore moves HEAD to a different commit, the index and working tree are reset to that commit, like
`git reset --hard`. Untracked files are kept; ignored files are never touched.

If HEAD's commit doesn't change, the index and working tree are left exactly as they are, even when other branches,
tags or remotes are restored.

To bring back uncommitted work as well, take the snapshot with `IncludeWorkingTree`; see
[Working tree snapshots](/GitSnapshotter/library/working-tree/).

## Nothing is changed if the restore can't finish

Before it changes anything, `Restore` checks two things:

1. **Every object the snapshot needs still exists.** If commits have been garbage-collected, or the snapshot was
   taken in a different repository, it throws `MissingObjectsException` listing the missing ids.
2. **No uncommitted work would be lost.** If resetting the working tree would overwrite changes that aren't
   committed, it throws `UncommittedChangesException` listing the paths.

In both cases the repository is untouched. The second check works like `git checkout`: a modified file only counts if
the restore would actually change it. A dirty working tree doesn't block a restore that leaves HEAD's commit where it
is.

To discard uncommitted changes deliberately, set `Force`:

```csharp
GitRepository.Restore(path, snapshot, new RestoreOptions { Force = true });
```

See [Error handling](/GitSnapshotter/library/error-handling/) for how to deal with each exception.

## Restoring part of a snapshot

`RestoreOptions` can leave whole parts of the repository alone, for example tags or HEAD. See
[Choosing what to restore](/GitSnapshotter/library/choosing-what-to-restore/).

## Restoring when a scope ends

`GitRepository.Preserve` takes a snapshot and returns a `SnapshotScope`. Disposing the scope restores the
snapshot, once:

```csharp
using (var scope = GitRepository.Preserve(path))
{
    RunReleaseScript(path);
    Console.WriteLine($"Will restore HEAD to {scope.Snapshot.Head}");
} // restored here, even if RunReleaseScript throws
```

`Preserve` takes the same options as `GetSnapshot` and `Restore`:

```csharp
using var scope = GitRepository.Preserve(
    path,
    snapshotOptions: new SnapshotOptions { IncludeWorkingTree = true },
    restoreOptions: new RestoreOptions { Force = true });
```

Disposing a second time does nothing. If the restore fails when the scope is disposed, the exception is thrown
from `Dispose`. This is the pattern [Testing](/GitSnapshotter/library/testing/) is built on.
