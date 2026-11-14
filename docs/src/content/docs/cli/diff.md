---
title: diff
description: List what changed in a repository since a snapshot was taken.
---

```text
GitSnapshotter.Console diff <repository> <snapshot-file>
```

Takes a fresh snapshot of `<repository>` and compares it with `<snapshot-file>`, printing one line per change. It
never changes the repository.

## Example

```console
$ GitSnapshotter.Console diff . ../before.json
~ Head HEAD main -> experiment
~ Branch feature b803aa0e577d422308d789b5a64ec784def50469 -> 7bb0b8d12decfd8d9f6deb827c519008c80dcf4c
+ Branch experiment 7bb0b8d12decfd8d9f6deb827c519008c80dcf4c
- Tag v1.0 b803aa0e577d422308d789b5a64ec784def50469 (annotation 2fd9fb7f8beecc53171b02184a760937711b3b70)
```

With no changes it prints `No changes since the snapshot.`

## Reading the output

Each line is `<kind> <category> <name> <value>`:

- `+` — added since the snapshot; the value is the new one.
- `-` — removed since the snapshot; the value is the old one.
- `~` — changed; shows `old -> new`.

| Category | Example |
|---|---|
| `Head` | `~ Head HEAD main -> (detached) 7bb0b8d…` |
| `Branch` | `+ Branch experiment 7bb0b8d…` |
| `Upstream` | `~ Upstream main origin refs/heads/main -> upstream refs/heads/main` |
| `Remote` | `~ Remote origin https://old.example/app.git -> https://new.example/app.git` |
| `RemoteBranch` | `~ RemoteBranch origin/main c4aa34b… -> d5e185f…` |
| `Tag` | `- Tag v1.0 b803aa0… (annotation 2fd9fb7…)` |
| `Reference` | `+ Reference refs/stash 91c2e0f…` |
| `WorkingTree` | `~ WorkingTree working directory 4b825dc… -> 8e1d7a0…` |

(Ids are shortened here; the tool prints them in full.)

Changes are grouped in that order. Within a group, removed and changed items come first, then added ones.

## Notes

- If the snapshot file includes the working tree, `diff` also captures the current working tree and reports
  `WorkingTree` changes, writing objects to the object database in the process. Otherwise uncommitted changes aren't
  reported at all.
- `diff` exits with `0` whether or not anything changed. See
  [Scripting](/GitSnapshotter/cli/scripting/#detecting-changes) to test for changes.
- `diff` shows changes since the snapshot; `restore --dry-run` shows the same changes reversed, from the restore's
  point of view.
