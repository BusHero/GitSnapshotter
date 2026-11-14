---
title: Comparing snapshots
description: List what changed between two snapshots, or preview what a restore would do.
---

`SnapshotDiff.Compare(before, after)` lists every difference between two snapshots:

```csharp
var before = GitRepository.GetSnapshot(path);

RunSomething(path);

var diff = SnapshotDiff.Compare(before, GitRepository.GetSnapshot(path));

if (diff.HasChanges)
{
    Console.WriteLine(diff);   // one change per line
}
```

```text
~ Head HEAD main -> experiment
~ Branch feature b803aa0e577d422308d789b5a64ec784def50469 -> 7bb0b8d12decfd8d9f6deb827c519008c80dcf4c
+ Branch experiment 7bb0b8d12decfd8d9f6deb827c519008c80dcf4c
- Tag v1.0 b803aa0e577d422308d789b5a64ec784def50469 (annotation 2fd9fb7f8beecc53171b02184a760937711b3b70)
```

Each line starts with `+` for something added, `-` for something removed and `~` for something changed, followed by
the category, the name, and the value or values.

## Working with changes

`diff.Changes` is a list of `SnapshotChange` records, grouped by category in this order: HEAD, branches, upstreams,
remotes, remote-tracking branches, tags, other references, working tree. Within a category, removed and changed items
come first, then added ones, each sorted by name.

```csharp
var movedBranches = diff.Changes
    .Where(c => c.Category == ChangeCategory.Branch && c.Kind == SnapshotChangeKind.Modified)
    .Select(c => c.Name);

var newTags = diff.Changes
    .Where(c => c.Category == ChangeCategory.Tag && c.Kind == SnapshotChangeKind.Added)
    .Select(c => c.Name);
```

| Property | Meaning |
|---|---|
| `Kind` | `Added`, `Removed` or `Modified` |
| `Category` | What kind of item changed (see below) |
| `Name` | The item's name |
| `Before` | The value in `before`, or `null` when the item was added |
| `After` | The value in `after`, or `null` when the item was removed |

## Categories and their values

| Category | `Name` | Value |
|---|---|---|
| `Head` | `HEAD` | The branch name, or `(detached) <commit>` |
| `Branch` | Branch name | Its tip commit |
| `Upstream` | Branch name | Remote and merge ref, e.g. `origin refs/heads/main` |
| `Remote` | Remote name | Its URL |
| `RemoteBranch` | e.g. `origin/main` | Its commit, or `-> origin/main` for `origin/HEAD` |
| `Tag` | Tag name | Its target, plus `(annotation <id>)` for annotated tags |
| `Reference` | Full name, e.g. `refs/stash` | Its target |
| `WorkingTree` | `index` or `working directory` | The tree id |

`HEAD` and `WorkingTree` changes are always `Modified`.

A tag whose annotation was recreated with a new id shows as `Modified` even though it points at the same commit.

`WorkingTree` changes only appear when **both** snapshots include the working tree. Comparing a snapshot taken with
`IncludeWorkingTree` against one taken without it reports no working tree changes.

## Previewing a restore

The arguments are "from" and "to". To see what restoring `snapshot` would change, compare the current state with
the snapshot, in that order:

```csharp
var current = GitRepository.GetSnapshot(path, new SnapshotOptions
{
    IncludeWorkingTree = snapshot.WorkingTree is not null,
});

var preview = SnapshotDiff.Compare(current, snapshot);
```

This is what the command line's `restore --dry-run` does.

The preview lists the reference changes. It doesn't tell you whether the restore would be blocked by uncommitted
changes; only `Restore` checks that.
