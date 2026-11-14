---
title: Introduction
description: What GitSnapshotter is, what a snapshot records, and when to use one.
---

GitSnapshotter records the state of a git repository, its references and optionally its uncommitted work, and later
puts the repository back exactly as it was. It comes as:

- **A .NET library**, the `GitSnapshotter` NuGet package, for use from code and tests. It runs on .NET 8 and later
  and on .NET Framework 4.7.2 and later, and is built on [LibGit2Sharp](https://github.com/libgit2/libgit2sharp).
- **A command-line tool**, `gitsnapshotter`, for use from a shell or a script.


## What a snapshot records

| Part | What is recorded |
|---|---|
| HEAD | The checked-out branch, or the commit when HEAD is detached |
| Local branches | Each branch's tip, and its upstream (`branch.<name>.remote` and `branch.<name>.merge`) |
| Remotes | Each remote's URL, its remote-tracking branches, and `refs/remotes/<remote>/HEAD` |
| Tags | Lightweight and annotated tags, including the annotation's object id and message |
| Other references | Everything else under `refs/`, such as `refs/stash` and `refs/notes/*` |
| Index and working tree | *Optional.* Staged content, modified files and untracked (non-ignored) files |

A snapshot holds **names and object ids, not content**. Taking one is cheap and the result is a small JSON
document. The commits themselves stay in the repository's object database, so a snapshot can only be restored
while the commits it refers to are still there. See [How it works](/GitSnapshotter/concepts/how-it-works/) for
what that means in practice.

## When to use it

**Tests that touch a real repository.** Tests for git tooling, release scripts or build steps often create
branches, commit, tag or add remotes. Wrap each test in `GitRepository.Preserve` and the repository is put back
afterwards, even when the test fails. See [Testing](/GitSnapshotter/library/testing/).

**An undo button for risky operations.** Take a snapshot before a scripted rebase, history rewrite or bulk branch
clean-up. If anything goes wrong, restore it. See [Scripting](/GitSnapshotter/cli/scripting/).

**Finding out what changed.** Compare two snapshots to see exactly which branches moved, which tags were added and
which remotes changed. See [Comparing snapshots](/GitSnapshotter/library/comparing-snapshots/).

## What it is not

GitSnapshotter is not a backup tool. It doesn't copy commits, files or the object database. If you need a copy of
the repository that survives `git gc` or deleting the folder, use `git clone --mirror` or `git bundle`.
Reflogs, hooks, most git configuration and submodule contents aren't recorded either; see
[Limitations](/GitSnapshotter/concepts/limitations/).
