using System.Text.Json;

using LibGit2Sharp;

namespace GitSnapshotter.Console;

/// <summary>The command-line interface. Kept separate from <c>Program</c> so it can be tested.</summary>
internal static class Cli
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int UsageError = 2;

    public const string Usage = """
        Usage:
          GitSnapshotter.Console snapshot <repository> [--output <file>] [--include-working-tree]
              Take a snapshot. Prints JSON, or writes it to <file>.
          GitSnapshotter.Console restore <repository> <snapshot-file> [--force] [--dry-run]
              Restore a snapshot. --force overwrites uncommitted changes;
              --dry-run only lists what would change.
          GitSnapshotter.Console diff <repository> <snapshot-file>
              List what changed in the repository since the snapshot was taken.
          GitSnapshotter.Console help
              Show this message.
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0)
        {
            error.WriteLine(Usage);
            return UsageError;
        }

        try
        {
            return args[0] switch
            {
                "help" or "--help" or "-h" => Help(output),
                "snapshot" => Snapshot(args[1..], output, error),
                "restore" => Restore(args[1..], output, error),
                "diff" => Diff(args[1..], output, error),
                // Earlier versions took just a repository path and printed its snapshot.
                _ when args.Length == 1 && Directory.Exists(args[0]) => Snapshot(args, output, error),
                _ => Fail(error, $"Unknown command '{args[0]}'."),
            };
        }
        catch (Exception exception) when (exception is GitSnapshotterException
                                              or LibGit2SharpException
                                              or IOException
                                              or UnauthorizedAccessException
                                              or JsonException
                                              or InvalidOperationException)
        {
            error.WriteLine($"error: {exception.Message}");
            return Failure;
        }
    }

    private static int Help(TextWriter output)
    {
        output.WriteLine(Usage);
        return Success;
    }

    private static int Snapshot(string[] args, TextWriter output, TextWriter error)
    {
        if (!TryParse(args, ["--include-working-tree"], ["--output", "-o"], out var positional, out var flags, out var values, out var message)
            || positional.Count != 1)
        {
            return Fail(error, message ?? "snapshot takes exactly one repository path.");
        }

        var snapshot = GitRepository.GetSnapshot(
            positional[0],
            new SnapshotOptions { IncludeWorkingTree = flags.Contains("--include-working-tree") });

        var file = values.GetValueOrDefault("--output") ?? values.GetValueOrDefault("-o");
        if (file is null)
        {
            output.WriteLine(snapshot.ToJson());
        }
        else
        {
            snapshot.Save(file);
            output.WriteLine($"Snapshot written to {file}.");
        }

        return Success;
    }

    private static int Restore(string[] args, TextWriter output, TextWriter error)
    {
        if (!TryParse(args, ["--force", "--dry-run"], [], out var positional, out var flags, out _, out var message)
            || positional.Count != 2)
        {
            return Fail(error, message ?? "restore takes a repository path and a snapshot file.");
        }

        var (repository, file) = (positional[0], positional[1]);
        var snapshot = GitRepositorySnapshot.Load(file);

        if (flags.Contains("--dry-run"))
        {
            var current = GitRepository.GetSnapshot(
                repository,
                new SnapshotOptions { IncludeWorkingTree = snapshot.WorkingTree is not null });
            WriteDiff(output, SnapshotDiff.Compare(current, snapshot), "Nothing to restore.");
            return Success;
        }

        GitRepository.Restore(repository, snapshot, new RestoreOptions { Force = flags.Contains("--force") });
        output.WriteLine($"Restored {repository} from {file}.");
        return Success;
    }

    private static int Diff(string[] args, TextWriter output, TextWriter error)
    {
        if (!TryParse(args, [], [], out var positional, out _, out _, out var message) || positional.Count != 2)
        {
            return Fail(error, message ?? "diff takes a repository path and a snapshot file.");
        }

        var snapshot = GitRepositorySnapshot.Load(positional[1]);
        var current = GitRepository.GetSnapshot(
            positional[0],
            new SnapshotOptions { IncludeWorkingTree = snapshot.WorkingTree is not null });

        WriteDiff(output, SnapshotDiff.Compare(snapshot, current), "No changes since the snapshot.");
        return Success;
    }

    private static void WriteDiff(TextWriter output, SnapshotDiff diff, string noChanges)
    {
        if (!diff.HasChanges)
        {
            output.WriteLine(noChanges);
            return;
        }

        foreach (var change in diff.Changes)
        {
            output.WriteLine(change);
        }
    }

    private static bool TryParse(
        string[] args,
        string[] knownFlags,
        string[] knownOptions,
        out List<string> positional,
        out HashSet<string> flags,
        out Dictionary<string, string> values,
        out string? error)
    {
        positional = [];
        flags = [];
        values = [];
        error = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (knownFlags.Contains(arg))
            {
                flags.Add(arg);
            }
            else if (knownOptions.Contains(arg))
            {
                if (i + 1 >= args.Length)
                {
                    error = $"{arg} needs a value.";
                    return false;
                }

                values[arg] = args[++i];
            }
            else if (arg.StartsWith('-'))
            {
                error = $"Unknown option '{arg}'.";
                return false;
            }
            else
            {
                positional.Add(arg);
            }
        }

        return true;
    }

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine($"error: {message}");
        error.WriteLine();
        error.WriteLine(Usage);
        return UsageError;
    }
}
