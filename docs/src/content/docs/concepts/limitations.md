---
title: Limitations and FAQ
description: What snapshots don't record, and answers to common questions.
sidebar:
  label: Limitations and FAQ
---

## Not recorded

| Not recorded | Notes |
|---|---|
| Commits and file content | A snapshot holds ids only; the objects must still be in the repository. See [How long a snapshot stays restorable](/GitSnapshotter/concepts/how-it-works/#how-long-a-snapshot-stays-restorable). |
| Older stash entries | Only `refs/stash`, the latest entry, is recorded. Older entries live in the stash's reflog. |
| Reflogs | Restoring adds new reflog entries; it doesn't bring back old ones. |
| Git configuration | Except remote URLs and branch upstreams. User settings, aliases, `core.*` and so on are untouched. |
| Remote push URLs and refspecs | A remote that has to be re-added gets the default fetch refspec. |
| Hooks, `info/exclude`, `.git/info/*` | |
| Submodule contents | Submodule commits recorded in the parent's trees are part of the commits; the submodules' own repositories aren't touched. |
| Ignored files | Never captured and never deleted. |
| An operation in progress | A merge, rebase, cherry-pick or bisect that is underway isn't recorded or restored. Abort it before restoring. |
| Symbolic references other than HEAD and `refs/remotes/*/HEAD` | Rare in practice. |
| Worktrees | Each linked worktree has its own HEAD and index; snapshot and restore each worktree separately. |

## Platform notes

- **.NET Framework**: symbolic links in the working tree are treated as plain files. Git checks them out that way on
  Windows unless `core.symlinks` is enabled.
- **Windows**: there is no executable bit; when capturing the working tree, a file's mode is taken from the index.

## FAQ

### Is a snapshot a backup?

No. It doesn't copy any commits or files, only the names and ids that point at them. If the repository folder is
deleted or garbage-collected, the snapshot can't bring it back. For a backup, use `git clone --mirror` or
`git bundle create backup.bundle --all`.

### Can I restore a snapshot into a different clone?

Yes, as long as that clone has all the objects the snapshot needs, for example after fetching everything from the
same remote. If something is missing, `Restore` throws `MissingObjectsException` and changes nothing. Working tree
snapshots only exist in the repository where they were taken.

### Does restoring push or fetch anything?

No. Everything happens locally. Remote-tracking branches such as `origin/main` are restored to where they were, which
is what your last fetch had seen; the remote itself is never contacted.

### Why did restore delete my new branch?

A restore makes the repository match the snapshot exactly, so branches, tags and remotes created afterwards are
removed. Use [`RestoreOptions`](/GitSnapshotter/library/choosing-what-to-restore/) to leave some kinds alone, or take
a new snapshot after creating things you want to keep. The commits are still in the object database for a while if
you need to get one back.

### Why does restore say I have uncommitted changes when I only changed a branch?

It only complains when the restore would move HEAD to a different commit, or restore the working tree, *and* that
would overwrite your changes. If you are on a branch whose tip the snapshot moves, that is the case. Commit or stash,
or use `Force`.

### Is restoring the same snapshot twice safe?

Yes. Restoring only changes what differs, so a second restore of the same snapshot does nothing.

### Do git hooks run?

No. GitSnapshotter uses libgit2 directly, not the `git` executable, so no hooks are triggered.
