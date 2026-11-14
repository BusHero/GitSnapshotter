namespace GitSnapshotter;

/// <summary>Controls what <see cref="GitRepository.GetSnapshot(string, SnapshotOptions?)"/> records.</summary>
public sealed record SnapshotOptions
{
    /// <summary>The default options: references only, no working tree.</summary>
    public static SnapshotOptions Default { get; } = new();

    /// <summary>
    /// Also record the index and the working directory (tracked and untracked, non-ignored files).
    /// This writes blob and tree objects to the repository's object database but changes no references or files.
    /// </summary>
    public bool IncludeWorkingTree { get; init; }
}

/// <summary>Controls what <see cref="GitRepository.Restore(string, GitRepositorySnapshot, RestoreOptions?)"/> changes.</summary>
public sealed record RestoreOptions
{
    /// <summary>The default options: restore everything the snapshot contains, refuse to discard uncommitted changes.</summary>
    public static RestoreOptions Default { get; } = new();

    /// <summary>Restore HEAD (the checked-out branch, or the detached commit).</summary>
    public bool Head { get; init; } = true;

    /// <summary>Restore local branches, their tips and their upstream configuration.</summary>
    public bool Branches { get; init; } = true;

    /// <summary>Restore remotes, their URLs and their remote-tracking branches.</summary>
    public bool Remotes { get; init; } = true;

    /// <summary>Restore tags.</summary>
    public bool Tags { get; init; } = true;

    /// <summary>Restore other references such as <c>refs/stash</c> and notes.</summary>
    public bool References { get; init; } = true;

    /// <summary>Restore the index and working tree when the snapshot contains them.</summary>
    public bool WorkingTree { get; init; } = true;

    /// <summary>
    /// Allow the restore to overwrite uncommitted changes to tracked files. Without it, a restore that
    /// would need to rewrite the working tree throws <see cref="UncommittedChangesException"/> if the
    /// working tree is dirty, and nothing is changed.
    /// </summary>
    public bool Force { get; init; }
}
