---
title: Comparing snapshots
description: List what changed between two snapshots, or preview a restore.
sidebar:
  order: 3
---

`SnapshotDiff.Compare(before, after)` lists every difference between two snapshots:

```csharp
var diff = SnapshotDiff.Compare(snapshot, GitRepository.GetSnapshot(path));

if (diff.HasChanges)
{
    foreach (var change in diff.Changes)
    {
        Console.WriteLine(change); // e.g. "+ Branch feature 3f2a…" or "~ Tag v1 aaa -> bbb"
    }
}
```

Each `SnapshotChange` has:

| Property | Meaning |
|---|---|
| `Kind` | `Added`, `Removed` or `Modified` |
| `Category` | `Head`, `Branch`, `Upstream`, `Remote`, `RemoteBranch`, `Tag`, `Reference` or `WorkingTree` |
| `Name` | The item's name |
| `Before` | The value in the earlier snapshot, or `null` when added |
| `After` | The value in the later snapshot, or `null` when removed |

`ToString()` prints `+` for added, `-` for removed and `~` for modified items.

## Preview a restore

To see what a restore would do, compare the current state with the snapshot:

```csharp
var preview = SnapshotDiff.Compare(GitRepository.GetSnapshot(path), snapshot);
```

If the snapshot includes the working tree, take the current snapshot with `IncludeWorkingTree = true` too, so the
working tree is compared.
