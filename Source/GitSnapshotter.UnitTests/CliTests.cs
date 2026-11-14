using GitSnapshotter.Console;

namespace GitSnapshotter.UnitTests;

public sealed class CliTests
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    private int Run(params string[] args) => Cli.Run(args, _output, _error);

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
    public async Task HelpPrintsUsage()
    {
        var exitCode = Run("help");

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(Cli.Success);
            await Assert.That(_output.ToString()).Contains("Usage:");
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
            await Assert.That(GitRepositorySnapshot.FromJson(_output.ToString())).IsEquivalentTo(repo.Snapshot());
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
            await Assert.That(GitRepositorySnapshot.FromJson(_output.ToString())).IsEquivalentTo(repo.Snapshot());
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
            await Assert.That(repo.Reopen().Branches["feature"]).IsNull();
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
            await Assert.That(repo.Reopen().Branches["feature"]).IsNotNull();
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
}
