---
title: Snapshot types
description: GitRepositorySnapshot, Branch, Remote, Tag and WorkingTreeState.
---

All snapshot types are immutable `sealed record`s in the `GitSnapshotter` namespace. Their JSON form is described in
[Snapshot format](/GitSnapshotter/concepts/snapshot-format/).

## GitRepositorySnapshot

```csharp
public sealed record GitRepositorySnapshot
```

The state of a repository's references at a point in time, plus optionally the index and working tree.

| Member | Type | Description |
|---|---|---|
| `Head` | `string` | *Required.* The checked-out branch's name, or the commit id when `IsHeadDetached` is `true`. |
| `IsHeadDetached` | `bool` | Whether HEAD pointed directly at a commit rather than a branch. |
| `Branches` | [`Branch[]`](#branch) | *Required.* Local branches, sorted by name. |
| `Remotes` | [`Remote[]`](#remote) | *Required.* Remotes, sorted by name. |
| `Tags` | [`Tag[]`](#tag) | *Required.* Tags, sorted by name. |
| `References` | `Dictionary<string, string>` | Direct references outside `refs/heads`, `refs/tags` and `refs/remotes`, keyed by full name, e.g. `refs/stash`. |
| `WorkingTree` | [`WorkingTreeState?`](#workingtreestate) | The index and working tree, or `null` if not captured. |

### Methods

| Method | Description |
|---|---|
| `string ToJson()` | Serializes the snapshot to indented JSON. |
| `static GitRepositorySnapshot FromJson(string json)` | Parses JSON written by `ToJson`. Throws `JsonException` if it isn't a valid snapshot. |
| `void Save(string path)` | Writes the JSON to a file, overwriting it if it exists. |
| `static GitRepositorySnapshot Load(string path)` | Reads a file written by `Save`. |

See [Saving and loading](/GitSnapshotter/library/saving-and-loading/).

## Branch

```csharp
public sealed record Branch
```

| Member | Type | Description |
|---|---|---|
| `Name` | `string` | *Required.* Short name, e.g. `main` or `feature/x`. |
| `Tip` | `string` | *Required.* The commit id the branch points at. |
| `TrackedBranch` | `string?` | Short name of the remote-tracking branch it follows, e.g. `origin/main`. |
| `RemoteName` | `string?` | The configured remote, `branch.<name>.remote`. |
| `UpstreamBranch` | `string?` | The upstream branch on the remote, `branch.<name>.merge`, e.g. `refs/heads/main`. |
| `Deconstruct(out string name, out string tip)` | | Enables `var (name, tip) = branch;` |

## Remote

```csharp
public sealed record Remote
```

| Member | Type | Description |
|---|---|---|
| `Name` | `string` | *Required.* The remote's name, e.g. `origin`. |
| `Url` | `string` | *Required.* The fetch URL. |
| `Branches` | `Dictionary<string, string>` | *Required.* Remote-tracking branches, keyed by short name (e.g. `origin/main`), with the commit each points at. |
| `Head` | `string?` | Short name of the branch `refs/remotes/<remote>/HEAD` points at, e.g. `origin/main`. |

## Tag

```csharp
public sealed record Tag
```

| Member | Type | Description |
|---|---|---|
| `Name` | `string` | *Required.* The tag's name. |
| `Target` | `string` | *Required.* The id of the object the tag points at. For an annotated tag, the annotation's target. |
| `Message` | `string?` | The annotation message, or `null` for a lightweight tag. |
| `AnnotationSha` | `string?` | The annotation object's id, or `null` for a lightweight tag. Restore reuses the object when it still exists. |
| `Deconstruct(out string name, out string target)` | | Enables `var (name, target) = tag;` |
| `Deconstruct(out string name, out string target, out string? message)` | | Enables `var (name, target, message) = tag;` |

## WorkingTreeState

```csharp
public sealed record WorkingTreeState
```

The index and working tree, stored as tree objects in the object database. The trees aren't referenced by anything,
so `git gc` eventually prunes them (after two weeks by default).

| Member | Type | Description |
|---|---|---|
| `IndexTree` | `string` | *Required.* The tree written from the index. |
| `WorkingDirectoryTree` | `string` | *Required.* The tree of tracked and untracked, non-ignored files in the working directory. |
