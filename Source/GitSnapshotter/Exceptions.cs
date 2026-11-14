namespace GitSnapshotter;

/// <summary>The base class for errors raised by GitSnapshotter.</summary>
public abstract class GitSnapshotterException : Exception
{
    /// <summary>Creates the exception.</summary>
    protected GitSnapshotterException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    protected GitSnapshotterException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    protected GitSnapshotterException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The snapshot refers to objects that are no longer in the repository's object database.
/// Nothing was changed.
/// </summary>
public sealed class MissingObjectsException : GitSnapshotterException
{
    /// <summary>Creates the exception.</summary>
    public MissingObjectsException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public MissingObjectsException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    public MissingObjectsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception for the given missing object ids.</summary>
    public MissingObjectsException(IReadOnlyList<string> missingObjects)
        : base($"The snapshot refers to objects missing from the repository: {string.Join(", ", missingObjects)}")
    {
        MissingObjects = missingObjects;
    }

    /// <summary>The ids of the missing objects.</summary>
    public IReadOnlyList<string> MissingObjects { get; } = [];
}

/// <summary>
/// The restore would overwrite uncommitted changes and <see cref="RestoreOptions.Force"/> was not set.
/// Nothing was changed.
/// </summary>
public sealed class UncommittedChangesException : GitSnapshotterException
{
    /// <summary>Creates the exception.</summary>
    public UncommittedChangesException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public UncommittedChangesException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    public UncommittedChangesException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception for the given changed paths.</summary>
    public UncommittedChangesException(IReadOnlyList<string> paths)
        : base($"The working tree has uncommitted changes that the restore would overwrite: {string.Join(", ", paths)}. "
               + "Commit or stash them, or restore with Force.")
    {
        Paths = paths;
    }

    /// <summary>The paths with uncommitted changes.</summary>
    public IReadOnlyList<string> Paths { get; } = [];
}
