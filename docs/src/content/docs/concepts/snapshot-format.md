---
title: Snapshot format
description: Every field of the snapshot JSON.
---

Snapshots are stored as UTF-8 JSON, written by `Save`, `ToJson` and `GitSnapshotter.Console snapshot`.
Property names are PascalCase, `null` values are left out, and lists are sorted by name with ordinal comparison.
Object ids are 40-character lowercase hexadecimal SHA-1 ids.

```json
{
  "Head": "main",
  "IsHeadDetached": false,
  "Branches": [
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
  "References": {
    "refs/stash": "91c2e0f4b4a1f2d0c6a7e3b5d8f9a0b1c2d3e4f5"
  },
  "WorkingTree": {
    "IndexTree": "8e1d7a0b6c5f4e3d2c1b0a9f8e7d6c5b4a3f2e1d",
    "WorkingDirectoryTree": "4b825dc642cb6eb9a060e54bf8d69288fbee4904"
  }
}
```

## Top level

| Field | Type | Required | Description |
|---|---|---|---|
| `Head` | string | yes | The checked-out branch's short name, or a commit id when `IsHeadDetached` is `true` |
| `IsHeadDetached` | boolean | no, default `false` | Whether HEAD pointed directly at a commit |
| `Branches` | array of [Branch](#branch) | yes | Local branches |
| `Remotes` | array of [Remote](#remote) | yes | Configured remotes |
| `Tags` | array of [Tag](#tag) | yes | Tags |
| `References` | object | no, default `{}` | Other direct references: full name → object id |
| `WorkingTree` | [WorkingTree](#workingtree) | no | Present only when taken with the working tree |

## Branch

| Field | Type | Required | Description |
|---|---|---|---|
| `Name` | string | yes | Short name, e.g. `main` or `feature/login` |
| `Tip` | string | yes | Commit id the branch points at |
| `TrackedBranch` | string | no | Short name of the remote-tracking branch it follows, e.g. `origin/main` |
| `RemoteName` | string | no | `branch.<name>.remote` |
| `UpstreamBranch` | string | no | `branch.<name>.merge`, e.g. `refs/heads/main` |

A branch without an upstream has none of the last three fields.

## Remote

| Field | Type | Required | Description |
|---|---|---|---|
| `Name` | string | yes | The remote's name, e.g. `origin` |
| `Url` | string | yes | The fetch URL |
| `Branches` | object | yes | Remote-tracking branches: short name (e.g. `origin/main`) → commit id |
| `Head` | string | no | What `refs/remotes/<name>/HEAD` points at, as a short name, e.g. `origin/main` |

## Tag

| Field | Type | Required | Description |
|---|---|---|---|
| `Name` | string | yes | The tag's name |
| `Target` | string | yes | Id of the object the tag points at; for annotated tags, the annotation's target |
| `Message` | string | no | The annotation message, for annotated tags |
| `AnnotationSha` | string | no | Id of the annotation object, for annotated tags |

A lightweight tag has neither `Message` nor `AnnotationSha`.

## WorkingTree

| Field | Type | Required | Description |
|---|---|---|---|
| `IndexTree` | string | yes | Tree written from the index (staged content) |
| `WorkingDirectoryTree` | string | yes | Tree of the working directory's tracked and untracked, non-ignored files |

## Compatibility

Readers ignore unknown properties, and fields added in later versions are optional, so older snapshot files keep
working. A snapshot can be written by the library and read by the command-line tool, and the other way round.

The format isn't designed for editing by hand, but it is plain JSON: you can, for example, delete a branch from
`Branches` to have a restore remove that branch.
