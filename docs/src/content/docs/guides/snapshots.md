---
title: Taking snapshots
description: Take snapshots, include uncommitted work, and save them as JSON.
sidebar:
  order: 1
---

## References only

```csharp
var snapshot = GitRepository.GetSnapshot(path);
```

This reads the repository and changes nothing.

## Include uncommitted work

```csharp
var snapshot = GitRepository.GetSnapshot(path, new SnapshotOptions { IncludeWorkingTree = true });
```

This writes the index and working directory to the object database as tree objects. No references or files are
changed. Restoring such a snapshot brings back staged content, modified files and untracked (non-ignored) files,
and deletes untracked files that were not there. Ignored files are never touched.

The trees aren't referenced by any ref, so `git gc` can prune them once they are older than `gc.pruneExpire`
(two weeks by default).

## Save to and load from disk

A snapshot is a plain record that serializes to indented JSON:

```csharp
snapshot.Save("snapshot.json");
var loaded = GitRepositorySnapshot.Load("snapshot.json");

string json = snapshot.ToJson();
var parsed = GitRepositorySnapshot.FromJson(json);
```

The file looks like this:

```json
{
  "Head": "main",
  "IsHeadDetached": false,
  "Branches": [
    {
      "Name": "main",
      "Tip": "3f2a…",
      "TrackedBranch": "origin/main",
      "RemoteName": "origin",
      "UpstreamBranch": "refs/heads/main"
    }
  ],
  "Remotes": [
    {
      "Branches": { "origin/main": "3f2a…" },
      "Url": "https://example.com/repo.git",
      "Name": "origin",
      "Head": "origin/main"
    }
  ],
  "Tags": [
    { "Name": "v1", "Target": "3f2a…", "Message": "First release\n", "AnnotationSha": "9b1c…" }
  ],
  "References": { "refs/stash": "c0d4…" }
}
```

When the working tree is included, a `WorkingTree` object with `IndexTree` and `WorkingDirectoryTree` ids is added.

## Using an open repository

Every method also has an overload that takes an open `LibGit2Sharp.IRepository`:

```csharp
using var repository = new LibGit2Sharp.Repository(path);
var snapshot = GitRepository.GetSnapshot(repository);
```
