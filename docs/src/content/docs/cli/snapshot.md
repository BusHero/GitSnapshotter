---
title: snapshot
description: Take a snapshot of a repository and print it or write it to a file.
---

```text
gitsnapshotter snapshot <repository> [--output <file>] [--include-working-tree]
```

Takes a snapshot of `<repository>`. Without `--output`, the snapshot is printed to standard output as JSON.

## Options

| Option | Description |
|---|---|
| `--output <file>`, `-o <file>` | Write the snapshot to `<file>` instead of printing it. An existing file is overwritten. |
| `--include-working-tree` | Also record the index and working tree: staged changes, modified files and untracked, non-ignored files. See [Working tree snapshots](/GitSnapshotter/library/working-tree/). |

## Examples

Print the snapshot of the current repository:

```console
$ gitsnapshotter snapshot .
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

Write it to a file next to the repository:

```console
$ gitsnapshotter snapshot . -o ../before.json
Snapshot written to ../before.json.
```

Include uncommitted work:

```sh
gitsnapshotter snapshot . --include-working-tree -o ../before.json
```

Pipe the JSON into another tool, for example to list branch names with [jq](https://jqlang.org):

```console
$ gitsnapshotter snapshot . | jq -r '.Branches[].Name'
feature
main
```

## Notes

- Write the file **outside** the repository. Inside, it is an untracked file: it appears in `git status`, and
  restoring a snapshot that includes the working tree deletes it.
- Taking a snapshot never changes the repository. With `--include-working-tree` it writes objects to the object
  database, which `git status` doesn't show.
- `--include-working-tree` fails with exit code `1` while the index has unresolved merge conflicts.
- Every field of the JSON is described in [Snapshot format](/GitSnapshotter/concepts/snapshot-format/).

Earlier versions took just a repository path. `gitsnapshotter <repository>` still works and is the same as
`gitsnapshotter snapshot <repository>`.
