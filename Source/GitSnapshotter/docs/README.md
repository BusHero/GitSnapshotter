# GitSnapshotter

Take a snapshot of a git repository's references, and later put the repository back exactly as it was.

A snapshot records:

- **HEAD**: the checked-out branch, or the commit when HEAD is detached
- **Local branches**: their tips and their upstream (`branch.<name>.remote` / `.merge`)
- **Remotes**: their URLs, remote-tracking branches, and `refs/remotes/<remote>/HEAD`
- **Tags**: lightweight and annotated, including the annotation object id
- **Other references**: anything else under `refs/`, such as `refs/stash` and `refs/notes/*`
- **Index and working tree** (optional): staged content, modified and untracked files

A snapshot stores names and object ids only, so it is small and cheap to take. It can only be restored while the
commits it refers to are still in the repository's object database.

Targets .NET 10. Built on [LibGit2Sharp](https://github.com/libgit2/libgit2sharp).

## Quick start

```csharp
using GitSnapshotter;

var snapshot = GitRepository.GetSnapshot("/path/to/repo");

// ... create branches, commit, move tags, add remotes ...

GitRepository.Restore("/path/to/repo", snapshot);
```

### Restore automatically (handy in tests)

```csharp
using (GitRepository.Preserve("/path/to/repo"))
{
    // anything done to the repository in here is undone on dispose
}
```

### Include uncommitted work

```csharp
var snapshot = GitRepository.GetSnapshot(path, new SnapshotOptions { IncludeWorkingTree = true });
```

This writes the index and working directory to the object database as tree objects. No references or files are
changed. Restoring such a snapshot brings back staged content, modified files and untracked (non-ignored) files,
and deletes untracked files that were not there. Ignored files are never touched.

The trees aren't referenced by any ref, so `git gc` can prune them once they are older than `gc.pruneExpire`
(two weeks by default).

### Save to and load from disk

```csharp
snapshot.Save("snapshot.json");
var loaded = GitRepositorySnapshot.Load("snapshot.json");

string json = snapshot.ToJson();
var parsed = GitRepositorySnapshot.FromJson(json);
```

### See what changed

```csharp
var diff = SnapshotDiff.Compare(snapshot, GitRepository.GetSnapshot(path));

foreach (var change in diff.Changes)
{
    Console.WriteLine(change); // e.g. "+ Branch feature 3f2a…" or "~ Tag v1 aaa -> bbb"
}
```

To preview what a restore would do, compare the current state with the snapshot:
`SnapshotDiff.Compare(current, snapshot)`.

## Restoring safely

`Restore` checks everything before changing anything:

- If the snapshot refers to objects the repository no longer has, it throws `MissingObjectsException`.
- If restoring would overwrite uncommitted changes, it throws `UncommittedChangesException` and lists the paths.
  This works like `git checkout`: only changes that would actually be lost count. Set `RestoreOptions.Force` to
  discard them.

In both cases the repository is left untouched.

When the checked-out branch's commit changes, the index and working tree are updated to match (a hard reset).
Untracked files are kept unless the snapshot includes the working tree.

### Choosing what to restore

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

Every method also has an overload that takes an open `LibGit2Sharp.IRepository`.

## Command line

The `GitSnapshotter.Console` tool wraps the library:

```text
GitSnapshotter.Console snapshot <repository> [--output <file>] [--include-working-tree]
GitSnapshotter.Console restore <repository> <snapshot-file> [--force] [--dry-run]
GitSnapshotter.Console diff <repository> <snapshot-file>
```

Exit codes: `0` success, `1` error (for example uncommitted changes or missing objects), `2` bad arguments.

## Limitations

- Restoring `refs/stash` brings back the latest stash only; older entries live in the stash reflog, which isn't recorded.
- Reflogs, git config other than remotes and branch upstreams, hooks and submodule contents aren't recorded.
- Remote push URLs and custom fetch refspecs aren't recorded. A remote that gets re-added uses the default refspec.
