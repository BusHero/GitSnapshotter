---
title: How it works
description: What a snapshot is, what restore does step by step, and how long a snapshot stays restorable.
---

## A snapshot is a list of names and ids

Almost everything in git that you think of as "the state of the repository" is a **reference**: a name that points
at an object id. `main` points at a commit, `v1.0` at a tag object, `origin/main` at the commit the remote had last
time you fetched, and HEAD at the branch you have checked out. The commits, trees and files themselves live in the
object database and never change once written.

A snapshot reads those references and writes down each name and the id it points at, along with the bits of
configuration that give branches and remotes their meaning (upstreams and remote URLs). Restoring makes the
references say the same thing again.

That's why snapshots are small, fast to take and fast to restore: no file content is copied. It is also why a
snapshot depends on the repository it came from: the ids are only useful while the objects are there.

### The working tree

Uncommitted work isn't in the object database yet, so `IncludeWorkingTree` puts it there: it writes the index and
the working directory as two tree objects, the same way `git stash` does, and records their ids. Nothing points at
those trees, so they don't change anything you can see.

## What restore does, in order

<ol>
<li>

**Check that every needed object exists.** The ids come from the parts being restored: branch tips, remote-tracking
branches, tag targets, other references, a detached HEAD's commit, and the working tree's trees. If any is missing,
it throws `MissingObjectsException`.

</li>
<li>

**Check that no uncommitted work would be lost.** It works out which commit HEAD will end on. If that differs from
the current one, or the working tree is being restored, it compares the index and working directory with what they
will become and throws `UncommittedChangesException` for any path whose uncommitted content would be overwritten or
deleted. `Force` skips this check.

</li>
<li>

**Detach HEAD** at the current commit, so that any branch, including the checked-out one, can be deleted or moved.

</li>
<li>

**Restore local branches.** Delete branches not in the snapshot, then create or move the rest.

</li>
<li>

**Restore remotes.** Remove remotes not in the snapshot, add missing ones, fix changed URLs, then make each remote's
remote-tracking branches and `HEAD` match.

</li>
<li>

**Restore upstreams.** After the remotes, because removing a remote also removes the upstream settings that use it.

</li>
<li>

**Restore tags**, reusing the original annotation objects when they still exist.

</li>
<li>

**Restore other references**, such as `refs/stash` and `refs/notes/*`.

</li>
<li>

**Restore HEAD**: attach it to the recorded branch, or point it at the recorded commit. With `Head = false`, HEAD is
reattached to the branch it was on, if that branch still exists. This step runs even if an earlier step failed, so
HEAD isn't left detached.

</li>
<li>

**Update the working tree.** If the snapshot has a working tree, put it back. Otherwise, if HEAD's commit changed,
reset the index and working tree to the new commit.

</li>
</ol>

Steps 1 and 2 are the only checks. They cover the failures you can predict, so in practice a restore either does
everything or nothing. An unexpected error in between, such as a locked file, can leave it half done; running the same
restore again finishes it, because each step only changes what differs.

## How long a snapshot stays restorable

A snapshot can be restored as long as the objects it points at exist. Git deletes objects only when garbage
collection (`git gc`, which git also runs automatically from time to time) finds them unreachable **and** old enough:

- **Commits that branches or tags still point at** never go away.
- **Commits that were only on deleted or rewritten branches** stay reachable through the reflog until its entries
  expire: 90 days by default (`gc.reflogExpire`), or 30 days for commits that are no longer on the branch's history
  (`gc.reflogExpireUnreachable`).
- **Unreachable objects** with no reflog entry, which includes **working tree snapshots**, are pruned once they are
  older than `gc.pruneExpire`, two weeks by default.

So a references-only snapshot is good for weeks, often months; a working tree snapshot for days. To keep the
objects alive longer, point a reference at them, for example with `git tag keep/before-migration <commit>`, or raise
the expiry settings.

## Where the repository is opened

Every operation opens the repository with LibGit2Sharp, a .NET wrapper around libgit2. It doesn't call the `git`
executable, so git doesn't need to be installed, and hooks don't run. Reference changes are written the same way git
writes them, so git, IDEs and other tools see them straight away.
