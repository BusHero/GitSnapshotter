using System.Security.Cryptography;
using System.Text;

using LibGit2Sharp;

namespace GitSnapshotter;

/// <summary>Captures and restores the index and working directory as tree objects.</summary>
internal static class WorkingTreeOperations
{
    private static readonly StatusOptions AllFiles = new()
    {
        IncludeUntracked = true,
        RecurseUntrackedDirs = true,
        IncludeIgnored = false,
        DetectRenamesInIndex = false,
        DetectRenamesInWorkDir = false,
    };

    private const FileStatus IndexChanges =
        FileStatus.NewInIndex | FileStatus.ModifiedInIndex | FileStatus.DeletedFromIndex
        | FileStatus.TypeChangeInIndex | FileStatus.RenamedInIndex;

    private const FileStatus WorkdirContentChanges =
        FileStatus.NewInWorkdir | FileStatus.ModifiedInWorkdir | FileStatus.TypeChangeInWorkdir
        | FileStatus.RenamedInWorkdir;

    public static WorkingTreeState Capture(IRepository repository)
    {
        if (repository.Index.Conflicts.Any())
        {
            throw new InvalidOperationException(
                "The index has unresolved merge conflicts, so the working tree cannot be captured.");
        }

        var indexTree = repository.ObjectDatabase.CreateTree(repository.Index);
        var workdir = TreeDefinition.From(indexTree);

        foreach (var entry in repository.RetrieveStatus(AllFiles))
        {
            if (entry.State.HasFlag(FileStatus.DeletedFromWorkdir))
            {
                workdir.Remove(entry.FilePath);
            }
            else if ((entry.State & WorkdirContentChanges) != 0)
            {
                var fullPath = FullPath(repository, entry.FilePath);
                if (Directory.Exists(fullPath))
                {
                    // Nested repositories and submodules are not captured.
                    continue;
                }

                using var content = new MemoryStream(ReadContent(fullPath));
                var blob = repository.ObjectDatabase.CreateBlob(content);
                workdir.Add(entry.FilePath, blob, GetMode(repository, entry.FilePath, fullPath));
            }
        }

        var workdirTree = repository.ObjectDatabase.CreateTree(workdir);

        return new WorkingTreeState
        {
            IndexTree = indexTree.Sha,
            WorkingDirectoryTree = workdirTree.Sha,
        };
    }

    /// <summary>
    /// Lists the paths whose uncommitted content would be lost if the index became
    /// <paramref name="targetIndex"/> and the working directory became <paramref name="targetWorkdir"/>.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="targetIndex">The tree the index will match, or <c>null</c> for an empty index.</param>
    /// <param name="targetWorkdir">The tree the working directory will match, or <c>null</c> for no tracked files.</param>
    /// <param name="removesUntracked"><c>true</c> when untracked files missing from <paramref name="targetWorkdir"/> will be deleted.</param>
    public static List<string> FindPathsThatWouldBeLost(
        IRepository repository,
        Tree? targetIndex,
        Tree? targetWorkdir,
        bool removesUntracked)
    {
        var lost = new List<string>();

        foreach (var entry in repository.RetrieveStatus(AllFiles))
        {
            var state = entry.State;
            var path = entry.FilePath;
            var isLost = false;

            if ((state & IndexChanges) != 0 && !state.HasFlag(FileStatus.DeletedFromIndex))
            {
                var staged = repository.Index[path]?.Id;
                isLost |= staged != targetIndex?[path]?.Target.Id;
            }

            if ((state & WorkdirContentChanges) != 0)
            {
                var fullPath = FullPath(repository, path);
                var target = targetWorkdir?[path];

                if (Directory.Exists(fullPath))
                {
                    isLost |= removesUntracked;
                }
                else if (target is null)
                {
                    var survives = state == FileStatus.NewInWorkdir && !removesUntracked;
                    isLost |= !survives;
                }
                else
                {
                    isLost |= HashBlob(ReadContent(fullPath)) != target.Target.Sha;
                }
            }

            if (isLost)
            {
                lost.Add(path);
            }
        }

        return lost;
    }

    /// <summary>Makes the index and working directory match <paramref name="state"/>.</summary>
    public static void Restore(IRepository repository, WorkingTreeState state, Commit? head)
    {
        var indexTree = repository.Lookup<Tree>(state.IndexTree);
        var workdirTree = repository.Lookup<Tree>(state.WorkingDirectoryTree);

        ResetHard(repository, head);

        foreach (var entry in repository.RetrieveStatus(AllFiles))
        {
            if (entry.State == FileStatus.NewInWorkdir)
            {
                DeletePath(FullPath(repository, entry.FilePath));
            }
        }

        var changes = repository.Diff.Compare<TreeChanges>(head?.Tree, workdirTree);
        foreach (var change in changes)
        {
            var fullPath = FullPath(repository, change.Path);

            switch (change.Status)
            {
                case ChangeKind.Deleted:
                    DeletePath(fullPath);
                    break;

                case ChangeKind.Added:
                case ChangeKind.Modified:
                case ChangeKind.TypeChanged:
                    WriteFile(repository, fullPath, change.Oid, change.Mode);
                    break;
            }
        }

        repository.Index.Replace(indexTree);
        repository.Index.Write();
    }

    /// <summary>Resets the index and working directory to <paramref name="commit"/>, or empties them when it is <c>null</c>.</summary>
    public static void ResetHard(IRepository repository, Commit? commit)
    {
        if (commit is not null)
        {
            repository.Reset(ResetMode.Hard, commit);
            return;
        }

        foreach (var entry in repository.Index)
        {
            DeletePath(FullPath(repository, entry.Path));
        }

        repository.Index.Clear();
        repository.Index.Write();
    }

    private static void WriteFile(IRepository repository, string fullPath, ObjectId blobId, Mode mode)
    {
        if (mode == Mode.GitLink)
        {
            return;
        }

        var blob = repository.Lookup<Blob>(blobId);
        DeletePath(fullPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        using var content = blob.GetContentStream();
#if NET
        if (mode == Mode.SymbolicLink && !OperatingSystem.IsWindows())
        {
            using var reader = new StreamReader(content, Encoding.UTF8);
            File.CreateSymbolicLink(fullPath, reader.ReadToEnd());
            return;
        }
#endif

        using (var file = File.Create(fullPath))
        {
            content.CopyTo(file);
        }

        if (mode == Mode.ExecutableFile && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(fullPath, File.GetUnixFileMode(fullPath)
                                          | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
    }

    private static void DeletePath(string fullPath)
    {
        var info = new FileInfo(fullPath);
        if (info.Exists || LinkTarget(info) is not null)
        {
            info.Delete();
        }
    }

    private static Mode GetMode(IRepository repository, string relativePath, string fullPath)
    {
        if (LinkTarget(new FileInfo(fullPath)) is not null)
        {
            return Mode.SymbolicLink;
        }

        if (OperatingSystem.IsWindows())
        {
            // Windows has no executable bit; keep whatever the index says.
            return repository.Index[relativePath]?.Mode ?? Mode.NonExecutableFile;
        }

        var executable = (File.GetUnixFileMode(fullPath) & UnixFileMode.UserExecute) != 0;
        return executable ? Mode.ExecutableFile : Mode.NonExecutableFile;
    }

    private static byte[] ReadContent(string fullPath)
    {
        var linkTarget = LinkTarget(new FileInfo(fullPath));
        return linkTarget is not null
            ? Encoding.UTF8.GetBytes(linkTarget)
            : File.ReadAllBytes(fullPath);
    }

    private static string HashBlob(byte[] content)
    {
        var header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");
        var buffer = new byte[header.Length + content.Length];
        header.CopyTo(buffer, 0);
        content.CopyTo(buffer, header.Length);

#pragma warning disable CA5350, CA1308 // git object ids are lowercase SHA-1 by definition
        return Convert.ToHexString(SHA1.HashData(buffer)).ToLowerInvariant();
#pragma warning restore CA5350, CA1308
    }

    /// <summary>The target of a symbolic link, or <c>null</c> for anything else.</summary>
    /// <remarks>
    /// .NET Framework has no API for this. It only runs on Windows, where git checks symbolic links out as plain
    /// files unless <c>core.symlinks</c> is set, so links are treated as plain files there.
    /// </remarks>
    private static string? LinkTarget(FileInfo file) =>
#if NET
        file.LinkTarget;
#else
        null;
#endif

    private static string FullPath(IRepository repository, string relativePath) =>
        Path.Combine(repository.Info.WorkingDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
