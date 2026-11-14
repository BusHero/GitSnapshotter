using LibGit2Sharp;

namespace GitSnapshotter;

/// <summary>Takes and restores snapshots of git repositories.</summary>
public static class GitRepository
{
    private const string HeadsPrefix = "refs/heads/";
    private const string TagsPrefix = "refs/tags/";
    private const string RemotesPrefix = "refs/remotes/";

    /// <summary>Takes a snapshot of the repository at <paramref name="pathToRepository"/> with the default options.</summary>
    public static GitRepositorySnapshot GetSnapshot(string pathToRepository) => GetSnapshot(pathToRepository, null);

    /// <summary>Takes a snapshot of the repository at <paramref name="pathToRepository"/>.</summary>
    /// <param name="pathToRepository">The repository's working directory or <c>.git</c> directory.</param>
    /// <param name="options">What to capture; <c>null</c> means <see cref="SnapshotOptions.Default"/>.</param>
    public static GitRepositorySnapshot GetSnapshot(string pathToRepository, SnapshotOptions? options)
    {
        using var repository = new Repository(pathToRepository);

        return GetSnapshot(repository, options);
    }

    /// <summary>Takes a snapshot of an open repository with the default options.</summary>
    public static GitRepositorySnapshot GetSnapshot(IRepository repository) => GetSnapshot(repository, null);

    /// <summary>Takes a snapshot of an open repository.</summary>
    /// <param name="repository">The repository.</param>
    /// <param name="options">What to capture; <c>null</c> means <see cref="SnapshotOptions.Default"/>.</param>
    public static GitRepositorySnapshot GetSnapshot(IRepository repository, SnapshotOptions? options)
    {
        ArgumentNullException.ThrowIfNull(repository);

        options ??= SnapshotOptions.Default;

        var isDetached = repository.Info.IsHeadDetached;
        var head = isDetached
            ? repository.Head.Tip.Sha
            : repository.Head.FriendlyName;

        var branches = repository.Branches
            .Where(b => !b.IsRemote)
            .Select(branch => new Branch
            {
                Tip = branch.Tip.Sha,
                TrackedBranch = branch.TrackedBranch?.FriendlyName,
                RemoteName = branch.RemoteName,
                UpstreamBranch = branch.UpstreamBranchCanonicalName,
                Name = branch.FriendlyName,
            })
            .OrderBy(b => b.Name, StringComparer.Ordinal)
            .ToArray();

        var remotes = repository.Network.Remotes
            .Select(remote => GetRemote(repository, remote))
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .ToArray();

        var tags = repository.Tags
            .Select(tag => new Tag
            {
                Target = tag.Target.Sha,
                Message = tag.Annotation?.Message,
                AnnotationSha = tag.Annotation?.Sha,
                Name = tag.FriendlyName,
            })
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

        return new GitRepositorySnapshot
        {
            Head = head,
            IsHeadDetached = isDetached,
            Branches = branches,
            Remotes = remotes,
            Tags = tags,
            References = GetOtherReferences(repository),
            WorkingTree = options.IncludeWorkingTree ? WorkingTreeOperations.Capture(repository) : null,
        };
    }

    /// <summary>
    /// Takes a snapshot now and returns a scope that restores it when disposed.
    /// </summary>
    /// <example>
    /// <code>
    /// using (GitRepository.Preserve(path))
    /// {
    ///     // change branches, tags, commits...
    /// } // the repository is restored here
    /// </code>
    /// </example>
    public static SnapshotScope Preserve(
        string pathToRepository,
        SnapshotOptions? snapshotOptions = null,
        RestoreOptions? restoreOptions = null)
    {
        var snapshot = GetSnapshot(pathToRepository, snapshotOptions);

        return new SnapshotScope(pathToRepository, snapshot, restoreOptions ?? RestoreOptions.Default);
    }

    /// <summary>
    /// Restores the repository at <paramref name="pathToRepository"/> to <paramref name="snapshot"/> with the default options.
    /// </summary>
    /// <exception cref="MissingObjectsException">The snapshot refers to objects the repository no longer has. Nothing was changed.</exception>
    /// <exception cref="UncommittedChangesException">The restore would overwrite uncommitted changes. Nothing was changed.</exception>
    public static void Restore(string pathToRepository, GitRepositorySnapshot snapshot) =>
        Restore(pathToRepository, snapshot, null);

    /// <summary>Restores the repository at <paramref name="pathToRepository"/> to <paramref name="snapshot"/>.</summary>
    /// <param name="pathToRepository">The repository's working directory or <c>.git</c> directory.</param>
    /// <param name="snapshot">The snapshot to restore.</param>
    /// <param name="options">What to restore; <c>null</c> means <see cref="RestoreOptions.Default"/>.</param>
    /// <exception cref="MissingObjectsException">The snapshot refers to objects the repository no longer has. Nothing was changed.</exception>
    /// <exception cref="UncommittedChangesException">The restore would overwrite uncommitted changes. Nothing was changed.</exception>
    public static void Restore(
        string pathToRepository,
        GitRepositorySnapshot snapshot,
        RestoreOptions? options)
    {
        using var repository = new Repository(pathToRepository);

        Restore(repository, snapshot, options);
    }

    /// <summary>Restores an open repository to <paramref name="snapshot"/> with the default options.</summary>
    /// <exception cref="MissingObjectsException">The snapshot refers to objects the repository no longer has. Nothing was changed.</exception>
    /// <exception cref="UncommittedChangesException">The restore would overwrite uncommitted changes. Nothing was changed.</exception>
    public static void Restore(IRepository repository, GitRepositorySnapshot snapshot) =>
        Restore(repository, snapshot, null);

    /// <summary>Restores an open repository to <paramref name="snapshot"/>.</summary>
    /// <param name="repository">The repository.</param>
    /// <param name="snapshot">The snapshot to restore.</param>
    /// <param name="options">What to restore; <c>null</c> means <see cref="RestoreOptions.Default"/>.</param>
    /// <exception cref="MissingObjectsException">The snapshot refers to objects the repository no longer has. Nothing was changed.</exception>
    /// <exception cref="UncommittedChangesException">The restore would overwrite uncommitted changes. Nothing was changed.</exception>
    public static void Restore(
        IRepository repository,
        GitRepositorySnapshot snapshot,
        RestoreOptions? options)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(snapshot);

        options ??= RestoreOptions.Default;

        EnsureObjectsExist(repository, snapshot, options);

        var originalHead = repository.Refs.Head;
        var originalHeadTarget = originalHead.TargetIdentifier;
        var originalCommit = repository.Head.Tip;
        var restoreWorkingTree = options.WorkingTree && snapshot.WorkingTree is not null;

        var finalCommit = PredictFinalHeadCommit(repository, snapshot, options);
        var resetNeeded = finalCommit?.Sha != originalCommit?.Sha;

        if ((resetNeeded || restoreWorkingTree) && !options.Force)
        {
            EnsureNothingIsLost(repository, snapshot, finalCommit, restoreWorkingTree);
        }

        // Detach HEAD first so any branch, including the checked-out one, can be deleted or moved.
        if (originalCommit is not null)
        {
            repository.Refs.UpdateTarget(repository.Refs.Head, originalCommit.Id);
        }

        try
        {
            if (options.Branches)
            {
                RestoreBranches(repository, snapshot.Branches);
            }

            if (options.Remotes)
            {
                RestoreRemotes(repository, snapshot.Remotes);
            }

            if (options.Branches)
            {
                // After remotes: removing a remote also clears upstream settings that use it.
                RestoreUpstreams(repository, snapshot.Branches);
            }

            if (options.Tags)
            {
                RestoreTags(repository, snapshot.Tags);
            }

            if (options.References)
            {
                RestoreOtherReferences(repository, snapshot.References);
            }
        }
        finally
        {
            if (options.Head)
            {
                RestoreHead(repository, snapshot);
            }
            else
            {
                ReattachHead(repository, originalHead, originalHeadTarget);
            }
        }

        var head = repository.Head.Tip;
        if (restoreWorkingTree)
        {
            WorkingTreeOperations.Restore(repository, snapshot.WorkingTree!, head);
        }
        else if (head?.Sha != originalCommit?.Sha)
        {
            WorkingTreeOperations.ResetHard(repository, head);
        }
    }

    private static Remote GetRemote(IRepository repository, LibGit2Sharp.Remote remote)
    {
        var prefix = $"{RemotesPrefix}{remote.Name}/";
        var references = repository.Refs
            .Where(r => r.CanonicalName.StartsWith(prefix, StringComparison.Ordinal))
            .ToArray();

        var branches = references
            .OfType<DirectReference>()
            .OrderBy(r => r.CanonicalName, StringComparer.Ordinal)
            .ToDictionary(r => r.CanonicalName[RemotesPrefix.Length..], r => r.TargetIdentifier);

        var head = references
            .OfType<SymbolicReference>()
            .FirstOrDefault(r => r.CanonicalName == prefix + "HEAD")
            ?.TargetIdentifier;

        return new Remote
        {
            Url = new Uri(remote.Url, UriKind.RelativeOrAbsolute),
            Branches = branches,
            Name = remote.Name,
            Head = head?.StartsWith(RemotesPrefix, StringComparison.Ordinal) == true
                ? head[RemotesPrefix.Length..]
                : head,
        };
    }

    private static Dictionary<string, string> GetOtherReferences(IRepository repository) =>
        repository.Refs
            .OfType<DirectReference>()
            .Where(r => IsOtherReference(r.CanonicalName))
            .OrderBy(r => r.CanonicalName, StringComparer.Ordinal)
            .ToDictionary(r => r.CanonicalName, r => r.TargetIdentifier);

    private static bool IsOtherReference(string canonicalName) =>
        canonicalName.StartsWith("refs/", StringComparison.Ordinal)
        && !canonicalName.StartsWith(HeadsPrefix, StringComparison.Ordinal)
        && !canonicalName.StartsWith(TagsPrefix, StringComparison.Ordinal)
        && !canonicalName.StartsWith(RemotesPrefix, StringComparison.Ordinal);

    private static void EnsureObjectsExist(
        IRepository repository,
        GitRepositorySnapshot snapshot,
        RestoreOptions options)
    {
        var required = new List<string>();

        if (options.Head && snapshot.IsHeadDetached)
        {
            required.Add(snapshot.Head);
        }

        if (options.Branches)
        {
            required.AddRange(snapshot.Branches.Select(b => b.Tip));
        }

        if (options.Remotes)
        {
            required.AddRange(snapshot.Remotes.SelectMany(r => r.Branches.Values));
        }

        if (options.Tags)
        {
            required.AddRange(snapshot.Tags.Select(t => t.Target));
        }

        if (options.References)
        {
            required.AddRange(snapshot.References.Values);
        }

        if (options.WorkingTree && snapshot.WorkingTree is not null)
        {
            required.Add(snapshot.WorkingTree.IndexTree);
            required.Add(snapshot.WorkingTree.WorkingDirectoryTree);
        }

        var missing = required
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(sha => !ObjectExists(repository, sha))
            .ToArray();

        if (missing.Length > 0)
        {
            throw new MissingObjectsException(missing);
        }
    }

    private static bool ObjectExists(IRepository repository, string sha) =>
        ObjectId.TryParse(sha, out var id) && repository.ObjectDatabase.Contains(id);

    /// <summary>Works out which commit HEAD will point at once the restore is done (<c>null</c> for an unborn branch).</summary>
    private static Commit? PredictFinalHeadCommit(
        IRepository repository,
        GitRepositorySnapshot snapshot,
        RestoreOptions options)
    {
        string? branch;

        if (options.Head)
        {
            if (snapshot.IsHeadDetached)
            {
                return repository.Lookup<Commit>(snapshot.Head);
            }

            branch = snapshot.Head;
        }
        else
        {
            if (repository.Info.IsHeadDetached)
            {
                return repository.Head.Tip;
            }

            branch = repository.Head.FriendlyName;
        }

        if (!options.Branches)
        {
            return repository.Branches[branch]?.Tip;
        }

        var tip = snapshot.Branches.FirstOrDefault(b => b.Name == branch)?.Tip;
        if (tip is not null)
        {
            return repository.Lookup<Commit>(tip);
        }

        // HEAD's branch is not in the snapshot: if HEAD is restored, the branch is unborn;
        // otherwise the branch gets deleted and HEAD stays detached at the current commit.
        return options.Head ? null : repository.Head.Tip;
    }

    private static void EnsureNothingIsLost(
        IRepository repository,
        GitRepositorySnapshot snapshot,
        Commit? finalCommit,
        bool restoreWorkingTree)
    {
        var lost = restoreWorkingTree
            ? WorkingTreeOperations.FindPathsThatWouldBeLost(
                repository,
                repository.Lookup<Tree>(snapshot.WorkingTree!.IndexTree),
                repository.Lookup<Tree>(snapshot.WorkingTree.WorkingDirectoryTree),
                removesUntracked: true)
            : WorkingTreeOperations.FindPathsThatWouldBeLost(
                repository,
                finalCommit?.Tree,
                finalCommit?.Tree,
                removesUntracked: false);

        if (lost.Count > 0)
        {
            throw new UncommittedChangesException(lost);
        }
    }

    private static void RestoreBranches(IRepository repository, IReadOnlyList<Branch> branches)
    {
        var wanted = branches.ToDictionary(b => b.Name, b => b.Tip);

        foreach (var branch in repository.Branches.Where(b => !b.IsRemote).ToArray())
        {
            if (!wanted.ContainsKey(branch.FriendlyName))
            {
                repository.Branches.Remove(branch);
            }
        }

        foreach (var (name, tip) in wanted)
        {
            SetDirectReference(repository, HeadsPrefix + name, tip);
        }
    }

    private static void RestoreUpstreams(IRepository repository, IReadOnlyList<Branch> branches)
    {
        foreach (var branch in branches)
        {
            var merge = branch.UpstreamBranch ?? GuessUpstreamBranch(branch);

            SetConfig(repository, $"branch.{branch.Name}.remote", branch.RemoteName);
            SetConfig(repository, $"branch.{branch.Name}.merge", branch.RemoteName is null ? null : merge);
        }
    }

    /// <summary>Snapshots written before <see cref="Branch.UpstreamBranch"/> existed only have the tracked branch's short name.</summary>
    private static string? GuessUpstreamBranch(Branch branch)
    {
        var prefix = $"{branch.RemoteName}/";
        if (branch.TrackedBranch is null || !branch.TrackedBranch.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return HeadsPrefix + branch.TrackedBranch[prefix.Length..];
    }

    private static void SetConfig(IRepository repository, string key, string? value)
    {
        var current = repository.Config.Get<string>(key, ConfigurationLevel.Local)?.Value;
        if (current == value)
        {
            return;
        }

        if (value is null)
        {
            repository.Config.Unset(key, ConfigurationLevel.Local);
        }
        else
        {
            repository.Config.Set(key, value, ConfigurationLevel.Local);
        }
    }

    private static void RestoreRemotes(IRepository repository, IReadOnlyList<Remote> remotes)
    {
        var wanted = remotes.ToDictionary(r => r.Name);

        foreach (var remote in repository.Network.Remotes.ToArray())
        {
            if (!wanted.ContainsKey(remote.Name))
            {
                repository.Network.Remotes.Remove(remote.Name);
            }
        }

        foreach (var remote in remotes)
        {
            var existing = repository.Network.Remotes[remote.Name];
            if (existing is null)
            {
                repository.Network.Remotes.Add(remote.Name, remote.Url.OriginalString);
            }
            else if (existing.Url != remote.Url.OriginalString)
            {
                repository.Network.Remotes.Update(remote.Name, r => r.Url = remote.Url.OriginalString);
            }

            RestoreRemoteTrackingBranches(repository, remote);
        }
    }

    private static void RestoreRemoteTrackingBranches(IRepository repository, Remote remote)
    {
        var prefix = $"{RemotesPrefix}{remote.Name}/";
        var headName = prefix + "HEAD";
        var wanted = remote.Branches.ToDictionary(x => RemotesPrefix + x.Key, x => x.Value);

        foreach (var reference in repository.Refs.Where(r => r.CanonicalName.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
        {
            if (reference.CanonicalName == headName)
            {
                continue;
            }

            if (!wanted.ContainsKey(reference.CanonicalName))
            {
                repository.Refs.Remove(reference);
            }
        }

        foreach (var (name, sha) in wanted)
        {
            SetDirectReference(repository, name, sha);
        }

        var head = repository.Refs[headName];
        if (remote.Head is null)
        {
            if (head is SymbolicReference)
            {
                repository.Refs.Remove(head);
            }
        }
        else if (head?.TargetIdentifier != RemotesPrefix + remote.Head)
        {
            repository.Refs.Add(headName, RemotesPrefix + remote.Head, allowOverwrite: true);
        }
    }

    private static void RestoreTags(IRepository repository, IReadOnlyList<Tag> tags)
    {
        var wanted = tags.ToDictionary(t => t.Name);

        foreach (var tag in repository.Tags.ToArray())
        {
            if (!wanted.ContainsKey(tag.FriendlyName))
            {
                repository.Tags.Remove(tag);
            }
        }

        foreach (var tag in tags)
        {
            RestoreTag(repository, tag);
        }
    }

    private static void RestoreTag(IRepository repository, Tag tag)
    {
        var canonicalName = TagsPrefix + tag.Name;
        var existing = repository.Tags[tag.Name];

        // Best case: the original annotation object is still there, so the tag comes back unchanged.
        if (tag.AnnotationSha is not null && ObjectExists(repository, tag.AnnotationSha))
        {
            SetDirectReference(repository, canonicalName, tag.AnnotationSha);
            return;
        }

        if (tag.Message is null)
        {
            if (existing is not null && (existing.IsAnnotated || existing.Target.Sha != tag.Target))
            {
                repository.Tags.Remove(existing);
            }

            SetDirectReference(repository, canonicalName, tag.Target);
            return;
        }

        if (existing is not null
            && existing.Target.Sha == tag.Target
            && existing.Annotation?.Message == tag.Message)
        {
            return;
        }

        // The annotation is gone: recreate one with the same target and message.
        var tagger = repository.Config.BuildSignature(DateTimeOffset.Now)
                     ?? new Signature("GitSnapshotter", "gitsnapshotter@localhost", DateTimeOffset.Now);

        repository.Tags.Add(tag.Name, tag.Target, tagger, tag.Message, allowOverwrite: true);
    }

    private static void RestoreOtherReferences(IRepository repository, Dictionary<string, string> references)
    {
        foreach (var reference in repository.Refs.OfType<DirectReference>().ToArray())
        {
            if (IsOtherReference(reference.CanonicalName) && !references.ContainsKey(reference.CanonicalName))
            {
                repository.Refs.Remove(reference);
            }
        }

        foreach (var (name, sha) in references)
        {
            SetDirectReference(repository, name, sha);
        }
    }

    private static void RestoreHead(IRepository repository, GitRepositorySnapshot snapshot)
    {
        if (snapshot.IsHeadDetached)
        {
            if (repository.Refs.Head.TargetIdentifier != snapshot.Head)
            {
                repository.Refs.UpdateTarget(repository.Refs.Head, new ObjectId(snapshot.Head));
            }

            return;
        }

        // Works for unborn branches too: libgit2 allows a symbolic HEAD to a branch that does not exist yet.
        repository.Refs.Add("HEAD", HeadsPrefix + snapshot.Head, allowOverwrite: true);
    }

    private static void ReattachHead(IRepository repository, Reference originalHead, string originalTarget)
    {
        if (originalHead is SymbolicReference && repository.Refs[originalTarget] is not null)
        {
            repository.Refs.Add("HEAD", originalTarget, allowOverwrite: true);
        }
    }

    private static void SetDirectReference(IRepository repository, string canonicalName, string sha)
    {
        var existing = repository.Refs[canonicalName];
        if (existing is DirectReference && string.Equals(existing.TargetIdentifier, sha, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        repository.Refs.Add(canonicalName, new ObjectId(sha), allowOverwrite: true);
    }
}
