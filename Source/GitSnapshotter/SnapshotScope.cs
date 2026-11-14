namespace GitSnapshotter;

/// <summary>
/// Restores a repository to the snapshot taken when the scope was created, once, when it is disposed.
/// Create one with <see cref="GitRepository.Preserve"/>.
/// </summary>
public sealed class SnapshotScope : IDisposable
{
    private readonly string _pathToRepository;
    private readonly RestoreOptions _restoreOptions;
    private bool _disposed;

    internal SnapshotScope(string pathToRepository, GitRepositorySnapshot snapshot, RestoreOptions restoreOptions)
    {
        _pathToRepository = pathToRepository;
        _restoreOptions = restoreOptions;
        Snapshot = snapshot;
    }

    /// <summary>The snapshot that will be restored.</summary>
    public GitRepositorySnapshot Snapshot { get; }

    /// <summary>Restores the snapshot. Later calls do nothing.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GitRepository.Restore(_pathToRepository, Snapshot, _restoreOptions);
    }
}
