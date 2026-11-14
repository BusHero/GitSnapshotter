---
title: Introduction
description: What GitSnapshotter records, and what it doesn't.
---

GitSnapshotter is a .NET library and command-line tool that records the state of a git repository and puts it back
later. It is built on [LibGit2Sharp](https://github.com/libgit2/libgit2sharp) and targets .NET 10.

## What a snapshot records

- **HEAD**: the checked-out branch, or the commit when HEAD is detached
- **Local branches**: their tips and their upstream (`branch.<name>.remote` / `.merge`)
- **Remotes**: their URLs, remote-tracking branches, and `refs/remotes/<remote>/HEAD`
- **Tags**: lightweight and annotated, including the annotation object id
- **Other references**: anything else under `refs/`, such as `refs/stash` and `refs/notes/*`
- **Index and working tree** (optional): staged content, modified and untracked files

A snapshot stores names and object ids only, so it is small and cheap to take. It can only be restored while the
commits it refers to are still in the repository's object database.

## When to use it

- **Tests** that create branches, commit, move tags or add remotes in a real repository and need to leave it as
  they found it.
- **Tooling** that rewrites history or experiments with refs and wants an undo button.
- **Inspection**: compare two snapshots to see exactly which refs moved.

## Limitations

- Restoring `refs/stash` brings back the latest stash only; older entries live in the stash reflog, which isn't
  recorded.
- Reflogs, git config other than remotes and branch upstreams, hooks and submodule contents aren't recorded.
- Remote push URLs and custom fetch refspecs aren't recorded. A remote that gets re-added uses the default refspec.
- Working-tree snapshots are stored as unreferenced tree objects, so `git gc` can prune them once they are older
  than `gc.pruneExpire` (two weeks by default).
