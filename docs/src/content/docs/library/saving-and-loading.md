---
title: Saving and loading
description: Store snapshots as JSON files or strings and read them back.
---

Snapshots serialize to indented JSON, so you can keep them on disk, put them in a database, or pass them between
processes, for example between the library and the [command-line tool](/GitSnapshotter/cli/overview/).

## Files

```csharp
snapshot.Save("before.json");                       // overwrites an existing file
var loaded = GitRepositorySnapshot.Load("before.json");

GitRepository.Restore(path, loaded);
```

Save snapshots **outside** the repository's working tree. A snapshot file inside it is an untracked file: it shows up
in `git status`, and restoring a snapshot that includes the working tree deletes it unless it is ignored.

## Strings

```csharp
string json = snapshot.ToJson();
GitRepositorySnapshot parsed = GitRepositorySnapshot.FromJson(json);
```

`FromJson` throws `System.Text.Json.JsonException` if the text isn't a snapshot, including when required fields such
as `Head` or `Branches` are missing. `Load` throws the same, plus the usual I/O exceptions if the file can't be read.

A round trip keeps everything: `GitRepositorySnapshot.FromJson(snapshot.ToJson())` describes the same state and
serializes to the same JSON.

## The format

Property names are PascalCase, fields that are `null` are left out, and collections are sorted by name:

```json
{
  "Head": "main",
  "IsHeadDetached": false,
  "Branches": [
    {
      "Name": "feature",
      "Tip": "b803aa0e577d422308d789b5a64ec784def50469"
    },
    {
      "Name": "main",
      "Tip": "d5e185f2a1c1650d1465637ef1f147ad4e2877ef",
      "TrackedBranch": "origin/main",
      "RemoteName": "origin",
      "UpstreamBranch": "refs/heads/main"
    }
  ],
  "Remotes": [
    {
      "Branches": {
        "origin/main": "c4aa34baa6e780cebb071ae274e3282e3d7bda78"
      },
      "Url": "https://github.com/example/app.git",
      "Name": "origin",
      "Head": "origin/main"
    }
  ],
  "Tags": [
    {
      "Name": "v1.0",
      "Target": "b803aa0e577d422308d789b5a64ec784def50469",
      "Message": "Release 1.0\n",
      "AnnotationSha": "2fd9fb7f8beecc53171b02184a760937711b3b70"
    }
  ],
  "References": {}
}
```

Every field is described in [Snapshot format](/GitSnapshotter/concepts/snapshot-format/).

## Snapshots from older versions

Snapshots written by earlier versions of GitSnapshotter can still be read and restored. Fields added since then are
simply missing; for example, a branch without `UpstreamBranch` gets its upstream worked out from `TrackedBranch`.
