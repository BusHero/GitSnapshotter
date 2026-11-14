---
title: Getting started
description: Install the package and take your first snapshot.
---

## Install

The `GitSnapshotter` package is published to GitHub Packages. Add the feed once (GitHub Packages needs a personal
access token with the `read:packages` scope):

```sh
dotnet nuget add source https://nuget.pkg.github.com/BusHero/index.json \
  --name BusHero --username <github-user> --password <token> --store-password-in-clear-text
```

Then reference the package:

```sh
dotnet add package GitSnapshotter --prerelease
```

Every push to `trunk` publishes a prerelease, so `--prerelease` picks up the latest build.

## Take and restore a snapshot

```csharp
using GitSnapshotter;

var snapshot = GitRepository.GetSnapshot("/path/to/repo");

// ... create branches, commit, move tags, add remotes ...

GitRepository.Restore("/path/to/repo", snapshot);
```

## Restore automatically

`Preserve` takes a snapshot now and restores it when the returned scope is disposed:

```csharp
using (GitRepository.Preserve("/path/to/repo"))
{
    // anything done to the repository in here is undone on dispose
}
```

## Next steps

- [Snapshots](/GitSnapshotter/guides/snapshots/): include the working tree, save to disk
- [Restoring](/GitSnapshotter/guides/restoring/): what is checked, and choosing what to restore
- [Comparing snapshots](/GitSnapshotter/guides/diff/): see what changed
- [Command line](/GitSnapshotter/guides/cli/): use it from a shell
