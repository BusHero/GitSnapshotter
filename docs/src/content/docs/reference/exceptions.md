---
title: Exceptions
description: GitSnapshotterException, MissingObjectsException and UncommittedChangesException.
---

```text
Exception
└── GitSnapshotterException
    ├── MissingObjectsException
    └── UncommittedChangesException
```

Both concrete exceptions are thrown by `Restore` (and by `SnapshotScope.Dispose`) **before** the repository is
changed. See [Error handling](/GitSnapshotter/library/error-handling/) for the exceptions GitSnapshotter passes
through from LibGit2Sharp, System.Text.Json and the file system.

## GitSnapshotterException

```csharp
public abstract class GitSnapshotterException : Exception
```

The base class for errors raised by GitSnapshotter. Catch it to handle both restore errors at once.

## MissingObjectsException

```csharp
public sealed class MissingObjectsException : GitSnapshotterException
```

The snapshot refers to objects that are no longer in the repository's object database. Nothing was changed.

| Member | Description |
|---|---|
| `IReadOnlyList<string> MissingObjects` | The ids of the missing objects |
| `MissingObjectsException(IReadOnlyList<string> missingObjects)` | Creates the exception |

Message: `The snapshot refers to objects missing from the repository: <id>, <id>, …`

## UncommittedChangesException

```csharp
public sealed class UncommittedChangesException : GitSnapshotterException
```

The restore would overwrite or delete uncommitted changes and `RestoreOptions.Force` was not set. Nothing was changed.

| Member | Description |
|---|---|
| `IReadOnlyList<string> Paths` | The affected paths, relative to the repository root, with `/` separators |
| `UncommittedChangesException(IReadOnlyList<string> paths)` | Creates the exception |

Message: `The working tree has uncommitted changes that the restore would overwrite: <path>, <path>. Commit or stash
them, or restore with Force.`
