namespace GitSnapshotter;

/// <summary>How an item differs between two snapshots.</summary>
public enum SnapshotChangeKind
{
    /// <summary>The item exists only in the later snapshot.</summary>
    Added,

    /// <summary>The item exists only in the earlier snapshot.</summary>
    Removed,

    /// <summary>The item exists in both snapshots with different values.</summary>
    Modified,
}

/// <summary>What kind of item changed.</summary>
public enum ChangeCategory
{
    /// <summary>HEAD (the value is the branch name, or the commit id when detached).</summary>
    Head,

    /// <summary>A local branch's tip.</summary>
    Branch,

    /// <summary>A local branch's upstream (the value is <c>remote</c> and <c>merge</c>, e.g. <c>origin refs/heads/main</c>).</summary>
    Upstream,

    /// <summary>A remote's URL.</summary>
    Remote,

    /// <summary>A remote-tracking branch, or a remote's symbolic HEAD.</summary>
    RemoteBranch,

    /// <summary>A tag (the value is the target, plus the annotation id for annotated tags).</summary>
    Tag,

    /// <summary>A reference outside branches, tags and remotes.</summary>
    Reference,

    /// <summary>The index or the working directory.</summary>
    WorkingTree,
}

/// <summary>One difference between two snapshots.</summary>
/// <param name="Kind">Whether the item was added, removed or modified.</param>
/// <param name="Category">What kind of item it is.</param>
/// <param name="Name">The item's name.</param>
/// <param name="Before">The value in the earlier snapshot, or <c>null</c> when added.</param>
/// <param name="After">The value in the later snapshot, or <c>null</c> when removed.</param>
public sealed record SnapshotChange(
    SnapshotChangeKind Kind,
    ChangeCategory Category,
    string Name,
    string? Before,
    string? After)
{
    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        SnapshotChangeKind.Added => $"+ {Category} {Name} {After}",
        SnapshotChangeKind.Removed => $"- {Category} {Name} {Before}",
        _ => $"~ {Category} {Name} {Before} -> {After}",
    };
}

/// <summary>The differences between two snapshots.</summary>
public sealed record SnapshotDiff
{
    private SnapshotDiff(IReadOnlyList<SnapshotChange> changes) => Changes = changes;

    /// <summary>The changes, grouped by category.</summary>
    public IReadOnlyList<SnapshotChange> Changes { get; }

    /// <summary><c>true</c> when the snapshots differ.</summary>
    public bool HasChanges => Changes.Count > 0;

    /// <summary>
    /// Lists what changed going from <paramref name="before"/> to <paramref name="after"/>.
    /// To preview what a restore would do, pass the current state as <paramref name="before"/>
    /// and the snapshot to restore as <paramref name="after"/>.
    /// </summary>
    public static SnapshotDiff Compare(GitRepositorySnapshot before, GitRepositorySnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var changes = new List<SnapshotChange>();

        if (HeadValue(before) != HeadValue(after))
        {
            changes.Add(new(SnapshotChangeKind.Modified, ChangeCategory.Head, "HEAD", HeadValue(before), HeadValue(after)));
        }

        Compare(changes, ChangeCategory.Branch,
            before.Branches.ToDictionary(b => b.Name, b => b.Tip),
            after.Branches.ToDictionary(b => b.Name, b => b.Tip));

        Compare(changes, ChangeCategory.Upstream,
            UpstreamValues(before),
            UpstreamValues(after));

        Compare(changes, ChangeCategory.Remote,
            before.Remotes.ToDictionary(r => r.Name, r => r.Url.OriginalString),
            after.Remotes.ToDictionary(r => r.Name, r => r.Url.OriginalString));

        Compare(changes, ChangeCategory.RemoteBranch,
            RemoteBranchValues(before),
            RemoteBranchValues(after));

        Compare(changes, ChangeCategory.Tag,
            before.Tags.ToDictionary(t => t.Name, TagValue),
            after.Tags.ToDictionary(t => t.Name, TagValue));

        Compare(changes, ChangeCategory.Reference, before.References, after.References);

        CompareWorkingTree(changes, before.WorkingTree, after.WorkingTree);

        return new SnapshotDiff(changes);
    }

    /// <inheritdoc />
    public override string ToString() => string.Join(Environment.NewLine, Changes);

    private static string HeadValue(GitRepositorySnapshot snapshot) =>
        snapshot.IsHeadDetached ? $"(detached) {snapshot.Head}" : snapshot.Head;

    private static string TagValue(Tag tag) =>
        tag.AnnotationSha is null ? tag.Target : $"{tag.Target} (annotation {tag.AnnotationSha})";

    private static Dictionary<string, string> UpstreamValues(GitRepositorySnapshot snapshot) =>
        snapshot.Branches
            .Where(b => b.RemoteName is not null)
            .ToDictionary(b => b.Name, b => $"{b.RemoteName} {b.UpstreamBranch}".TrimEnd());

    private static Dictionary<string, string> RemoteBranchValues(GitRepositorySnapshot snapshot)
    {
        var values = new Dictionary<string, string>();

        foreach (var remote in snapshot.Remotes)
        {
            foreach (var (name, sha) in remote.Branches)
            {
                values[name] = sha;
            }

            if (remote.Head is not null)
            {
                values[$"{remote.Name}/HEAD"] = $"-> {remote.Head}";
            }
        }

        return values;
    }

    private static void CompareWorkingTree(
        List<SnapshotChange> changes,
        WorkingTreeState? before,
        WorkingTreeState? after)
    {
        if (before is null || after is null)
        {
            // A snapshot taken without the working tree says nothing about it.
            return;
        }

        if (before.IndexTree != after.IndexTree)
        {
            changes.Add(new(SnapshotChangeKind.Modified, ChangeCategory.WorkingTree, "index", before.IndexTree, after.IndexTree));
        }

        if (before.WorkingDirectoryTree != after.WorkingDirectoryTree)
        {
            changes.Add(new(SnapshotChangeKind.Modified, ChangeCategory.WorkingTree, "working directory",
                before.WorkingDirectoryTree, after.WorkingDirectoryTree));
        }
    }

    private static void Compare(
        List<SnapshotChange> changes,
        ChangeCategory category,
        Dictionary<string, string> before,
        Dictionary<string, string> after)
    {
        foreach (var (name, value) in before.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (!after.TryGetValue(name, out var newValue))
            {
                changes.Add(new(SnapshotChangeKind.Removed, category, name, value, null));
            }
            else if (newValue != value)
            {
                changes.Add(new(SnapshotChangeKind.Modified, category, name, value, newValue));
            }
        }

        foreach (var (name, value) in after.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (!before.ContainsKey(name))
            {
                changes.Add(new(SnapshotChangeKind.Added, category, name, null, value));
            }
        }
    }
}
