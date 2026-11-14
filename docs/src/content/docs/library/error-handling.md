---
title: Error handling
description: The exceptions GitSnapshotter throws, and what state the repository is in afterwards.
---

## The two restore errors

Both derive from `GitSnapshotterException`, and both are thrown **before anything is changed**, so the repository is
exactly as it was.

### `MissingObjectsException`

The snapshot refers to objects that aren't in the repository's object database. `MissingObjects` lists their ids.

Common causes:

- The commits were garbage-collected. Once no branch, tag or other reference points at a commit, `git gc` removes
  it after its reflog entries expire (90 days by default, 30 for commits that aren't on the current branch's history). Working tree snapshots are affected sooner; see
  [Working tree snapshots](/GitSnapshotter/library/working-tree/).
- The snapshot was taken in a different repository, or a fresh clone that never fetched those commits.

```csharp
catch (MissingObjectsException e)
{
    Console.Error.WriteLine($"Can't restore: {e.MissingObjects.Count} objects are gone.");
}
```

If you only need some parts of the snapshot, restore just those; the check only covers the parts you restore. See
[Choosing what to restore](/GitSnapshotter/library/choosing-what-to-restore/).

### `UncommittedChangesException`

The restore would overwrite or delete uncommitted work and `RestoreOptions.Force` isn't set. `Paths` lists the files
concerned, relative to the repository root.

```csharp
try
{
    GitRepository.Restore(path, snapshot);
}
catch (UncommittedChangesException e)
{
    Console.Error.WriteLine("Commit or stash these first, or restore with Force:");
    foreach (var file in e.Paths)
    {
        Console.Error.WriteLine($"  {file}");
    }
}
```

To keep the changes, commit or stash them and restore again. To throw them away, restore with
`new RestoreOptions { Force = true }`.

## Other errors

GitSnapshotter doesn't wrap errors from LibGit2Sharp or the file system:

| Exception | When |
|---|---|
| `LibGit2Sharp.RepositoryNotFoundException` | The path isn't a git repository |
| `LibGit2Sharp.LibGit2SharpException` | Other git errors, such as a locked reference file |
| `InvalidOperationException` | Capturing the working tree while the index has unresolved merge conflicts |
| `System.Text.Json.JsonException` | `FromJson` or `Load` got something that isn't a valid snapshot |
| `IOException`, `UnauthorizedAccessException` | `Save` or `Load` can't access the file |

An error from git or the file system *during* a restore (after the checks have passed) can leave the restore half
done. Restore isn't transactional, but it is idempotent: once the cause is fixed, running the same restore again
finishes the job.

## Catching everything GitSnapshotter-specific

```csharp
catch (GitSnapshotterException e)
{
    // MissingObjectsException or UncommittedChangesException; nothing was changed
}
```
