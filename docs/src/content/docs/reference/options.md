---
title: Options
description: SnapshotOptions and RestoreOptions.
---

Both are immutable records with `init` properties. Use an object initializer, or start from `Default` and change it
with `with`.

## SnapshotOptions

```csharp
public sealed record SnapshotOptions
```

Controls what `GetSnapshot` records.

| Property | Type | Default | Description |
|---|---|---|---|
| `IncludeWorkingTree` | `bool` | `false` | Also record the index and the working directory: tracked and untracked, non-ignored files. Writes blob and tree objects to the object database but changes no references or files. |
| `Default` | `SnapshotOptions` | | *Static.* An instance with the defaults: references only. |

```csharp
var options = new SnapshotOptions { IncludeWorkingTree = true };
```

See [Working tree snapshots](/GitSnapshotter/library/working-tree/).

## RestoreOptions

```csharp
public sealed record RestoreOptions
```

Controls what `Restore` changes.

| Property | Type | Default | Description |
|---|---|---|---|
| `Head` | `bool` | `true` | Restore HEAD: the checked-out branch, or the detached commit. When off, HEAD stays on its current branch. |
| `Branches` | `bool` | `true` | Restore local branches, their tips and their upstream configuration. |
| `Remotes` | `bool` | `true` | Restore remotes, their URLs, their remote-tracking branches and `refs/remotes/<remote>/HEAD`. |
| `Tags` | `bool` | `true` | Restore tags. |
| `References` | `bool` | `true` | Restore other references, such as `refs/stash` and `refs/notes/*`. |
| `WorkingTree` | `bool` | `true` | Restore the index and working tree, when the snapshot contains them. |
| `Force` | `bool` | `false` | Allow the restore to overwrite or delete uncommitted changes. Without it, a restore that would lose them throws `UncommittedChangesException` and changes nothing. |
| `Default` | `RestoreOptions` | | *Static.* An instance with the defaults: restore everything, don't discard uncommitted changes. |

```csharp
var options = RestoreOptions.Default with { Tags = false, Force = true };
```

Parts that are switched off are also skipped by the safety checks. See
[Choosing what to restore](/GitSnapshotter/library/choosing-what-to-restore/).
