using GitSnapshotter.Console;

namespace GitSnapshotter.UnitTests;

public sealed class CliTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    private int Run(params string[] args) => Cli.Run(args, _output, _error);

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    [Test]
    public async Task NoArgumentsPrintsUsage()
    {
        var exitCode = Run();

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.UsageError);
            await Assert.That(_error.ToString()).Contains("Usage:");
        }
    }

    [Test]
    [Arguments("help")]
    [Arguments("--help")]
    [Arguments("-h")]
    public async Task HelpPrintsUsage(string command)
    {
        var exitCode = Run(command);

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.Success);
            await Assert.That(_output.ToString()).Contains("Usage:");
            await Assert.That(_error.ToString()).IsEmpty();
        }
    }

    [Test]
    [Arguments("help", "restore")]
    [Arguments("restore", "--help")]
    public async Task HelpForACommandPrintsItsUsage(params string[] args)
    {
        var exitCode = Run(args);

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.Success);
            await Assert.That(_output.ToString())
                .Contains($"Usage:{Environment.NewLine}  gitsnapshotter restore <repository> <snapshot-file>");
            await Assert.That(_output.ToString()).Contains("--dry-run");
            await Assert.That(_error.ToString()).IsEmpty();
        }
    }

    [Test]
    public async Task HelpForAnUnknownCommandIsAUsageError()
    {
        var exitCode = Run("help", "frobnicate");

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.UsageError);
            await Assert.That(_error.ToString()).Contains("frobnicate");
        }
    }

    [Test]
    public async Task VersionPrintsTheVersion()
    {
        var exitCode = Run("--version");

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.Success);
            await Assert.That(_output.ToString().Trim()).IsNotEmpty();
            await Assert.That(_error.ToString()).IsEmpty();
        }
    }

    [Test]
    public async Task UnknownCommandIsAUsageError()
    {
        await Assert.That(Run("frobnicate", "x")).IsEqualTo(Cli.UsageError);
    }

    [Test]
    public async Task UnknownOptionIsAUsageError()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();

        var exitCode = Run("snapshot", repo.Info.WorkingDirectory, "--bogus");

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.UsageError);
            await Assert.That(_error.ToString()).Contains("--bogus");
        }
    }

    [Test]
    public async Task SnapshotPrintsJson()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");

        var exitCode = Run("snapshot", repo.Info.WorkingDirectory);

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.Success);
            await SnapshotAssert.AreEquivalent(GitRepositorySnapshot.FromJson(_output.ToString()), repo.Snapshot());
        }
    }

    [Test]
    public async Task RepositoryPathAloneStillPrintsSnapshot()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");

        var exitCode = Run(repo.Info.WorkingDirectory);

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.Success);
            await SnapshotAssert.AreEquivalent(GitRepositorySnapshot.FromJson(_output.ToString()), repo.Snapshot());
        }
    }

    [Test]
    public async Task SnapshotWritesFile()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.WriteFile("a.txt", "wip");
        var file = GitTasks.GetTempPath() + ".json";

        var exitCode = Run("snapshot", repo.Info.WorkingDirectory, "--output", file, "--include-working-tree");

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.Success);
            await Assert.That(GitRepositorySnapshot.Load(file).WorkingTree).IsNotNull();
        }
    }

    [Test]
    public async Task RestoreRestoresTheRepository()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var file = GitTasks.GetTempPath() + ".json";
        repo.Snapshot().Save(file);
        repo.AddBranch("feature");

        var exitCode = Run("restore", repo.Info.WorkingDirectory, file);

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.Success);
            using var reopened = repo.Reopen();
            await Assert.That(reopened.Branches["feature"]).IsNull();
        }
    }

    [Test]
    public async Task RestoreDryRunListsChangesWithoutApplyingThem()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var file = GitTasks.GetTempPath() + ".json";
        repo.Snapshot().Save(file);
        repo.AddBranch("feature");

        var exitCode = Run("restore", repo.Info.WorkingDirectory, file, "--dry-run");

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.Success);
            await Assert.That(_output.ToString()).Contains("- Branch feature");
            using var reopened = repo.Reopen();
            await Assert.That(reopened.Branches["feature"]).IsNotNull();
        }
    }

    [Test]
    public async Task RestoreRefusingUncommittedChangesFails()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var file = GitTasks.GetTempPath() + ".json";
        repo.Snapshot().Save(file);
        repo.CommitFile("a.txt", "2");
        repo.WriteFile("a.txt", "uncommitted");

        var exitCode = Run("restore", repo.Info.WorkingDirectory, file);

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.Failure);
            await Assert.That(_error.ToString()).Contains("uncommitted changes");
        }

        await Assert.That(Run("restore", repo.Info.WorkingDirectory, file, "--force")).IsEqualTo(Cli.Success);
        await Assert.That(repo.ReadFile("a.txt")).IsEqualTo("1");
    }

    [Test]
    public async Task DiffListsChangesSinceSnapshot()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var file = GitTasks.GetTempPath() + ".json";
        repo.Snapshot().Save(file);
        repo.AddTag("v1");

        var exitCode = Run("diff", repo.Info.WorkingDirectory, file);

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.Success);
            await Assert.That(_output.ToString()).Contains("+ Tag v1");
        }
    }

    [Test]
    public async Task DiffWithoutChangesSaysSo()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var file = GitTasks.GetTempPath() + ".json";
        repo.Snapshot().Save(file);

        Run("diff", repo.Info.WorkingDirectory, file);

        await Assert.That(_output.ToString()).Contains("No changes");
    }

    [Test]
    public async Task MissingRepositoryIsAnError()
    {
        var exitCode = Run("snapshot", GitTasks.GetTempPath());

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.Failure);
            await Assert.That(_error.ToString()).StartsWith("error:");
        }
    }

    [Test]
    public async Task MissingSnapshotFileIsAnError()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();

        await Assert.That(Run("restore", repo.Info.WorkingDirectory, GitTasks.GetTempPath() + ".json"))
            .IsEqualTo(Cli.Failure);
    }

    [Test]
    public async Task UnknownCommandNamesTheCommandAndPrintsUsage()
    {
        Run("frobnicate", "x");

        using (Assert.Multiple())
        {
            await Assert.That(_error.ToString()).Contains("error: Unrecognized command or argument 'frobnicate'.");
            await Assert.That(_error.ToString()).Contains("Usage:");
            await Assert.That(_output.ToString()).IsEmpty();
        }
    }

    [Test]
    public async Task RepositoryPathWithMoreArgumentsIsNotTreatedAsASnapshot()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");

        var exitCode = Run(repo.Info.WorkingDirectory, "--output");

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.UsageError);
            await Assert.That(_output.ToString()).IsEmpty();
        }
    }

    [Test]
    [Arguments("snapshot")]
    [Arguments("snapshot", "one", "two")]
    public async Task SnapshotNeedsExactlyOneRepository(params string[] args)
    {
        var exitCode = Run(args);

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.UsageError);
            await Assert.That(_error.ToString()).Contains($"Usage:{Environment.NewLine}  gitsnapshotter snapshot <repository>");
        }
    }

    [Test]
    [Arguments("restore")]
    [Arguments("restore", "repo")]
    [Arguments("restore", "repo", "file", "extra")]
    public async Task RestoreNeedsARepositoryAndASnapshotFile(params string[] args)
    {
        var exitCode = Run(args);

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.UsageError);
            await Assert.That(_error.ToString()).Contains($"Usage:{Environment.NewLine}  gitsnapshotter restore <repository> <snapshot-file>");
        }
    }

    [Test]
    [Arguments("diff")]
    [Arguments("diff", "repo")]
    [Arguments("diff", "repo", "file", "extra")]
    public async Task DiffNeedsARepositoryAndASnapshotFile(params string[] args)
    {
        var exitCode = Run(args);

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.UsageError);
            await Assert.That(_error.ToString()).Contains($"Usage:{Environment.NewLine}  gitsnapshotter diff <repository> <snapshot-file>");
        }
    }

    [Test]
    [Arguments("restore", "--include-working-tree")]
    [Arguments("diff", "--force")]
    [Arguments("diff", "--dry-run")]
    [Arguments("snapshot", "--force")]
    public async Task OptionsOfOtherCommandsAreRejected(string command, string option)
    {
        var exitCode = Run(command, "repo", "file", option);

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.UsageError);
            await Assert.That(_error.ToString()).Contains($"error: Unrecognized command or argument '{option}'.");
        }
    }

    [Test]
    [Arguments("--output")]
    [Arguments("-o")]
    public async Task OutputWithoutAValueIsAUsageError(string option)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();

        var exitCode = Run("snapshot", repo.Info.WorkingDirectory, option);

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.UsageError);
            await Assert.That(_error.ToString()).Contains($"error: Required argument missing for option: '{option}'.");
        }
    }

    [Test]
    public async Task SnapshotWritesFileWithShortOutputOption()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var file = GitTasks.GetTempPath() + ".json";

        var exitCode = Run("snapshot", "-o", file, repo.Info.WorkingDirectory);

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.Success);
            await Assert.That(_output.ToString().Trim()).IsEqualTo($"Snapshot written to {file}.");
            await SnapshotAssert.AreEquivalent(GitRepositorySnapshot.Load(file), repo.Snapshot());
        }
    }

    [Test]
    public async Task SnapshotLeavesOutWorkingTreeByDefault()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.WriteFile("a.txt", "wip");

        Run("snapshot", repo.Info.WorkingDirectory);

        await Assert.That(GitRepositorySnapshot.FromJson(_output.ToString()).WorkingTree).IsNull();
    }

    [Test]
    public async Task SnapshotOfADirectoryThatIsNotARepositoryIsAnError()
    {
        var directory = Directory.CreateDirectory(GitTasks.GetTempPath()).FullName;

        var exitCode = Run("snapshot", directory);

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.Failure);
            await Assert.That(_error.ToString()).StartsWith("error:");
            await Assert.That(_output.ToString()).IsEmpty();
        }
    }

    [Test]
    public async Task RestoreReportsWhatItRestored()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var file = GitTasks.GetTempPath() + ".json";
        repo.Snapshot().Save(file);

        Run("restore", repo.Info.WorkingDirectory, file);

        await Assert.That(_output.ToString().Trim()).IsEqualTo($"Restored {repo.Info.WorkingDirectory} from {file}.");
    }

    [Test]
    public async Task RestoreDryRunWithoutChangesSaysSo()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var file = GitTasks.GetTempPath() + ".json";
        repo.Snapshot().Save(file);

        var exitCode = Run("restore", repo.Info.WorkingDirectory, file, "--dry-run");

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.Success);
            await Assert.That(_output.ToString().Trim()).IsEqualTo("Nothing to restore.");
        }
    }

    [Test]
    public async Task RestoreDryRunDoesNotTouchUncommittedChanges()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var file = GitTasks.GetTempPath() + ".json";
        repo.Snapshot().Save(file);
        repo.CommitFile("a.txt", "2");
        repo.WriteFile("a.txt", "uncommitted");

        var exitCode = Run("restore", repo.Info.WorkingDirectory, file, "--dry-run", "--force");

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.Success);
            await Assert.That(_output.ToString()).Contains("~ Branch master");
            await Assert.That(repo.ReadFile("a.txt")).IsEqualTo("uncommitted");
        }
    }

    [Test]
    public async Task RestoreRestoresTheWorkingTreeFromASnapshotThatHasOne()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.WriteFile("a.txt", "wip");
        var file = GitTasks.GetTempPath() + ".json";
        await Assert.That(Run("snapshot", repo.Info.WorkingDirectory, "-o", file, "--include-working-tree"))
            .IsEqualTo(Cli.Success);
        repo.WriteFile("a.txt", "other");

        var exitCode = Run("restore", repo.Info.WorkingDirectory, file, "--force");

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.Success);
            await Assert.That(repo.ReadFile("a.txt")).IsEqualTo("wip");
        }
    }

    [Test]
    public async Task DiffIncludesWorkingTreeWhenTheSnapshotHasOne()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var file = GitTasks.GetTempPath() + ".json";
        Run("snapshot", repo.Info.WorkingDirectory, "--output", file, "--include-working-tree");
        repo.WriteFile("a.txt", "wip");
        _output.GetStringBuilder().Clear();

        var exitCode = Run("diff", repo.Info.WorkingDirectory, file);

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.Success);
            await Assert.That(_output.ToString()).Contains("~ WorkingTree working directory");
        }
    }

    [Test]
    public async Task DiffIgnoresWorkingTreeWhenTheSnapshotHasNone()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var file = GitTasks.GetTempPath() + ".json";
        repo.Snapshot().Save(file);
        repo.WriteFile("a.txt", "wip");

        Run("diff", repo.Info.WorkingDirectory, file);

        await Assert.That(_output.ToString()).Contains("No changes");
    }

    [Test]
    [Arguments("not json")]
    [Arguments("null")]
    public async Task InvalidSnapshotFileIsAnError(string content, CancellationToken cancellationToken)
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var file = GitTasks.GetTempPath() + ".json";
        await File.WriteAllTextAsync(file, content, cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(Run("restore", repo.Info.WorkingDirectory, file)).IsEqualTo(Cli.Failure);
            await Assert.That(Run("diff", repo.Info.WorkingDirectory, file)).IsEqualTo(Cli.Failure);
            await Assert.That(_error.ToString()).StartsWith("error:");
            await Assert.That(_output.ToString()).IsEmpty();
        }
    }
}
