using System.CommandLine;
using System.CommandLine.Help;
using System.Text.Json;

using LibGit2Sharp;

namespace GitSnapshotter.Console;

/// <summary>The command-line interface. Kept separate from <c>Program</c> so it can be tested.</summary>
internal static class Cli
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int UsageError = 2;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        var root = BuildRootCommand(output, error);

        // Earlier versions took just a repository path and printed its snapshot.
        if (args is [var path] && root.Subcommands.All(command => command.Name != path) && Directory.Exists(path))
        {
            args = ["snapshot", path];
        }

        var configuration = new InvocationConfiguration
        {
            Output = output,
            Error = error,
            // Expected failures are reported below as "error: <message>"; anything else is a bug and should crash.
            EnableDefaultExceptionHandler = false,
        };

        var result = root.Parse(args);

        // --help wins over other mistakes on the command line, as it does in the dotnet CLI.
        if (result.Errors.Count > 0 && result.Action is not HelpAction)
        {
            foreach (var parseError in result.Errors)
            {
                error.WriteLine($"error: {parseError.Message}");
            }

            error.WriteLine();
            WriteHelp(root, result.CommandResult.Command, error);
            return UsageError;
        }

        try
        {
            return result.Invoke(configuration);
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

    private static Command BuildRootCommand(TextWriter output, TextWriter error)
    {
        // Named here rather than taken from the executable, which is GitSnapshotter.Console.dll (or the test host).
        var root = new Command(
            "gitsnapshotter",
            "Takes a snapshot of a git repository's branches, tags, remotes, HEAD and (optionally) working tree, "
            + "and restores it later.")
        {
            new HelpOption(),
            new VersionOption(),
        };

        root.Subcommands.Add(SnapshotCommand(output));
        root.Subcommands.Add(RestoreCommand(output));
        root.Subcommands.Add(DiffCommand(output));
        root.Subcommands.Add(HelpCommand(root, output));

        // With no command, show the help on stderr and exit with a usage error, as before.
        root.SetAction(_ =>
        {
            WriteHelp(root, root, error);
            return UsageError;
        });

        return root;
    }

    private static Command SnapshotCommand(TextWriter output)
    {
        var repository = RepositoryArgument();
        var outputFile = new Option<string>("--output", "-o")
        {
            Description = "Write the snapshot to this file instead of printing it.",
            HelpName = "file",
        };
        var includeWorkingTree = new Option<bool>("--include-working-tree")
        {
            Description = "Also record the index and the working tree.",
        };

        var command = new Command("snapshot", "Take a snapshot. Prints JSON, or writes it to the --output file.")
        {
            repository,
            outputFile,
            includeWorkingTree,
        };

        command.SetAction(result =>
        {
            var snapshot = GitRepository.GetSnapshot(
                result.GetRequiredValue(repository),
                new SnapshotOptions { IncludeWorkingTree = result.GetValue(includeWorkingTree) });

            var file = result.GetValue(outputFile);
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
        });

        return command;
    }

    private static Command RestoreCommand(TextWriter output)
    {
        var repository = RepositoryArgument();
        var snapshotFile = SnapshotFileArgument();
        var force = new Option<bool>("--force")
        {
            Description = "Overwrite uncommitted changes.",
        };
        var dryRun = new Option<bool>("--dry-run")
        {
            Description = "Only list what would change.",
        };

        var command = new Command("restore", "Restore a snapshot.")
        {
            repository,
            snapshotFile,
            force,
            dryRun,
        };

        command.SetAction(result =>
        {
            var path = result.GetRequiredValue(repository);
            var file = result.GetRequiredValue(snapshotFile);
            var snapshot = GitRepositorySnapshot.Load(file);

            if (result.GetValue(dryRun))
            {
                var current = GitRepository.GetSnapshot(
                    path,
                    new SnapshotOptions { IncludeWorkingTree = snapshot.WorkingTree is not null });
                WriteDiff(output, SnapshotDiff.Compare(current, snapshot), "Nothing to restore.");
                return Success;
            }

            GitRepository.Restore(path, snapshot, new RestoreOptions { Force = result.GetValue(force) });
            output.WriteLine($"Restored {path} from {file}.");
            return Success;
        });

        return command;
    }

    private static Command DiffCommand(TextWriter output)
    {
        var repository = RepositoryArgument();
        var snapshotFile = SnapshotFileArgument();

        var command = new Command("diff", "List what changed in the repository since the snapshot was taken.")
        {
            repository,
            snapshotFile,
        };

        command.SetAction(result =>
        {
            var snapshot = GitRepositorySnapshot.Load(result.GetRequiredValue(snapshotFile));
            var current = GitRepository.GetSnapshot(
                result.GetRequiredValue(repository),
                new SnapshotOptions { IncludeWorkingTree = snapshot.WorkingTree is not null });

            WriteDiff(output, SnapshotDiff.Compare(snapshot, current), "No changes since the snapshot.");
            return Success;
        });

        return command;
    }

    private static Command HelpCommand(Command root, TextWriter output)
    {
        var name = new Argument<string?>("command")
        {
            Description = "The command to show help for.",
            HelpName = "command",
            Arity = ArgumentArity.ZeroOrOne,
        };
        name.AcceptOnlyFromAmong([.. root.Subcommands.Select(command => command.Name)]);

        var command = new Command("help", "Show help for gitsnapshotter or one of its commands.") { name };

        command.SetAction(result =>
        {
            var target = result.GetValue(name) is { } commandName
                ? root.Subcommands.First(subcommand => subcommand.Name == commandName)
                : root;
            WriteHelp(root, target, output);
            return Success;
        });

        return command;
    }

    private static Argument<string> RepositoryArgument() =>
        new("repository") { Description = "Path to the git repository." };

    private static Argument<string> SnapshotFileArgument() =>
        new("snapshot-file") { Description = "A snapshot written by the snapshot command." };

    private static void WriteHelp(Command root, Command command, TextWriter writer)
    {
        string[] helpArgs = command == root ? ["--help"] : [command.Name, "--help"];
        root.Parse(helpArgs).Invoke(new InvocationConfiguration { Output = writer });
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
}
