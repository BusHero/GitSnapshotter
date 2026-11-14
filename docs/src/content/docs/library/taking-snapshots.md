---
title: Taking snapshots
description: Take a snapshot of a repository and read what it contains.
---

```csharp
using GitSnapshotter;

GitRepositorySnapshot snapshot = GitRepository.GetSnapshot("/path/to/repo");
```

`GetSnapshot` opens the repository, reads its references and closes it again. It changes nothing: no refs, no
files, no configuration. Pass the repository's working directory: the folder that contains `.git`.

## What you get back

`GitRepositorySnapshot` is an immutable record. Its collections are sorted by name, so two snapshots of the same
state serialize to the same JSON.

```csharp
Console.WriteLine(snapshot.IsHeadDetached
    ? $"Detached at {snapshot.Head}"
    : $"On branch {snapshot.Head}");

foreach (var branch in snapshot.Branches)
{
    var upstream = branch.TrackedBranch is null ? "" : $" (tracks {branch.TrackedBranch})";
    Console.WriteLine($"{branch.Name} {branch.Tip[..7]}{upstream}");
}

foreach (var remote in snapshot.Remotes)
{
    Console.WriteLine($"{remote.Name} {remote.Url}, {remote.Branches.Count} remote-tracking branches");
}

foreach (var (name, target, message) in snapshot.Tags)
{
    Console.WriteLine(message is null ? $"{name} -> {target}" : $"{name} -> {target}: {message.Trim()}");
}

foreach (var (reference, target) in snapshot.References)
{
    Console.WriteLine($"{reference} -> {target}");   // e.g. refs/stash, refs/notes/commits
}
```

| Property | Holds |
|---|---|
| `Head` | The checked-out branch's name, or a commit id when `IsHeadDetached` is `true` |
| `IsHeadDetached` | Whether HEAD pointed straight at a commit |
| `Branches` | Local branches: `Name`, `Tip`, and the upstream in `TrackedBranch`, `RemoteName`, `UpstreamBranch` |
| `Remotes` | Remotes: `Name`, `Url`, remote-tracking `Branches` (short name → commit id) and `Head` |
| `Tags` | Tags: `Name`, `Target`, and for annotated tags `Message` and `AnnotationSha` |
| `References` | Every other direct reference under `refs/`, keyed by full name |
| `WorkingTree` | The index and working tree, or `null` unless you asked for them |

The full list of members is in the [API reference](/GitSnapshotter/reference/snapshot-types/).

### Special cases

- **Detached HEAD**: `Head` is the commit id and `IsHeadDetached` is `true`.
- **A new repository with no commits**: `Head` is the name of the unborn branch (for example `main`) and
  `Branches` is empty. Restoring such a snapshot makes HEAD point at the unborn branch again.
- **Annotated tags**: `Target` is the commit the tag points at, not the annotation object; the annotation's own id
  is in `AnnotationSha`.
- **`refs/remotes/<remote>/HEAD`** is a symbolic reference. It is stored in the remote's `Head` (for example
  `origin/main`), not in its `Branches`.
- **Other references** only include direct references. Branches, tags and remote-tracking branches aren't repeated
  there.

## Including uncommitted work

By default only references are recorded. To also record the index and the working directory:

```csharp
var snapshot = GitRepository.GetSnapshot(path, new SnapshotOptions { IncludeWorkingTree = true });
```

This writes objects to the repository's object database but changes no references or files. See
[Working tree snapshots](/GitSnapshotter/library/working-tree/) for exactly what is captured and how long it lasts.

## Using an already open repository

If you already use LibGit2Sharp, pass the open repository instead of a path. Every `GitRepository` method has
this overload except `Preserve`.

```csharp
using LibGit2Sharp;

using var repository = new Repository(path);
var snapshot = GitRepository.GetSnapshot(repository);

// ... work with the repository ...

GitRepository.Restore(repository, snapshot);
```

GitSnapshotter doesn't dispose a repository you pass in.

## Deriving a modified snapshot

Snapshots are records, so you can derive a modified copy with `with`:

```csharp
// The same state, but without the tags
var withoutTags = snapshot with { Tags = [] };
```

Restoring `withoutTags` would delete every tag. To leave tags alone instead, use
[`RestoreOptions.Tags = false`](/GitSnapshotter/library/choosing-what-to-restore/).

:::note
Because the collections are arrays, `==` on two snapshots compares the arrays by reference, not by content. To check
whether two snapshots describe the same state, compare `ToJson()` or use
[`SnapshotDiff.Compare(a, b).HasChanges`](/GitSnapshotter/library/comparing-snapshots/).
:::
