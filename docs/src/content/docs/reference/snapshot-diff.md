---
title: SnapshotDiff
description: SnapshotDiff, SnapshotChange, SnapshotChangeKind and ChangeCategory.
---

## SnapshotDiff

```csharp
public sealed record SnapshotDiff
```

The differences between two snapshots. Create one with `Compare`.

| Member | Description |
|---|---|
| `static SnapshotDiff Compare(GitRepositorySnapshot before, GitRepositorySnapshot after)` | Lists what changed going from `before` to `after`. |
| `IReadOnlyList<SnapshotChange> Changes { get; }` | The changes, grouped by category. |
| `bool HasChanges { get; }` | `true` when there is at least one change. |
| `string ToString()` | The changes, one per line. |

To preview a restore, pass the current state as `before` and the snapshot to restore as `after`. See
[Comparing snapshots](/GitSnapshotter/library/comparing-snapshots/).

## SnapshotChange

```csharp
public sealed record SnapshotChange(
    SnapshotChangeKind Kind,
    ChangeCategory Category,
    string Name,
    string? Before,
    string? After);
```

One difference.

| Property | Description |
|---|---|
| `Kind` | Whether the item was added, removed or modified |
| `Category` | What kind of item it is |
| `Name` | The item's name |
| `Before` | The value in the earlier snapshot, or `null` when added |
| `After` | The value in the later snapshot, or `null` when removed |

`ToString()` returns `+ <Category> <Name> <After>`, `- <Category> <Name> <Before>` or
`~ <Category> <Name> <Before> -> <After>`.

## SnapshotChangeKind

```csharp
public enum SnapshotChangeKind { Added, Removed, Modified }
```

| Value | Meaning |
|---|---|
| `Added` | The item exists only in the later snapshot |
| `Removed` | The item exists only in the earlier snapshot |
| `Modified` | The item exists in both, with different values |

## ChangeCategory

```csharp
public enum ChangeCategory { Head, Branch, Upstream, Remote, RemoteBranch, Tag, Reference, WorkingTree }
```

| Value | `Name` | Value in `Before` / `After` |
|---|---|---|
| `Head` | `HEAD` | Branch name, or `(detached) <commit>` |
| `Branch` | Branch name | Tip commit id |
| `Upstream` | Branch name | `<remote> <merge>`, e.g. `origin refs/heads/main` |
| `Remote` | Remote name | Fetch URL |
| `RemoteBranch` | e.g. `origin/main`, `origin/HEAD` | Commit id, or `-> <branch>` for a remote's HEAD |
| `Tag` | Tag name | Target id, plus ` (annotation <id>)` for annotated tags |
| `Reference` | Full name, e.g. `refs/stash` | Object id |
| `WorkingTree` | `index` or `working directory` | Tree id |
