using FluentAssertions;
using FluentAssertions.Execution;

using GitSnapshotter.Console;

namespace GitSnapshotter.UnitTests;

public sealed class CliTests
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    private int Run(params string[] args) => Cli.Run(args, _output, _error);

    [Test]
    public void NoArgumentsPrintsUsage()
    {
        var exitCode = Run();

        using (new AssertionScope())
        {
            exitCode.Should().Be(Cli.UsageError);
            _error.ToString().Should().Contain("Usage:");
        }
    }

    [Test]
    public void HelpPrintsUsage()
    {
        var exitCode = Run("help");

        using (new AssertionScope())
        {
            exitCode.Should().Be(Cli.Success);
            _output.ToString().Should().Contain("Usage:");
        }
    }

    [Test]
    public void UnknownCommandIsAUsageError()
    {
        Run("frobnicate", "x").Should().Be(Cli.UsageError);
    }

    [Test]
    public void UnknownOptionIsAUsageError()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();

        var exitCode = Run("snapshot", repo.Info.WorkingDirectory, "--bogus");

        using (new AssertionScope())
        {
            exitCode.Should().Be(Cli.UsageError);
            _error.ToString().Should().Contain("--bogus");
        }
    }

    [Test]
    public void SnapshotPrintsJson()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");

        var exitCode = Run("snapshot", repo.Info.WorkingDirectory);

        using (new AssertionScope())
        {
            exitCode.Should().Be(Cli.Success);
            GitRepositorySnapshot.FromJson(_output.ToString()).Should().BeEquivalentTo(repo.Snapshot());
        }
    }

    [Test]
    public void RepositoryPathAloneStillPrintsSnapshot()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");

        var exitCode = Run(repo.Info.WorkingDirectory);

        using (new AssertionScope())
        {
            exitCode.Should().Be(Cli.Success);
            GitRepositorySnapshot.FromJson(_output.ToString()).Should().BeEquivalentTo(repo.Snapshot());
        }
    }

    [Test]
    public void SnapshotWritesFile()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        repo.WriteFile("a.txt", "wip");
        var file = GitTasks.GetTempPath() + ".json";

        var exitCode = Run("snapshot", repo.Info.WorkingDirectory, "--output", file, "--include-working-tree");

        using (new AssertionScope())
        {
            exitCode.Should().Be(Cli.Success);
            GitRepositorySnapshot.Load(file).WorkingTree.Should().NotBeNull();
        }
    }

    [Test]
    public void RestoreRestoresTheRepository()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var file = GitTasks.GetTempPath() + ".json";
        repo.Snapshot().Save(file);
        repo.AddBranch("feature");

        var exitCode = Run("restore", repo.Info.WorkingDirectory, file);

        using (new AssertionScope())
        {
            exitCode.Should().Be(Cli.Success);
            repo.Reopen().Branches["feature"].Should().BeNull();
        }
    }

    [Test]
    public void RestoreDryRunListsChangesWithoutApplyingThem()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var file = GitTasks.GetTempPath() + ".json";
        repo.Snapshot().Save(file);
        repo.AddBranch("feature");

        var exitCode = Run("restore", repo.Info.WorkingDirectory, file, "--dry-run");

        using (new AssertionScope())
        {
            exitCode.Should().Be(Cli.Success);
            _output.ToString().Should().Contain("- Branch feature");
            repo.Reopen().Branches["feature"].Should().NotBeNull();
        }
    }

    [Test]
    public void RestoreRefusingUncommittedChangesFails()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var file = GitTasks.GetTempPath() + ".json";
        repo.Snapshot().Save(file);
        repo.CommitFile("a.txt", "2");
        repo.WriteFile("a.txt", "uncommitted");

        var exitCode = Run("restore", repo.Info.WorkingDirectory, file);

        using (new AssertionScope())
        {
            exitCode.Should().Be(Cli.Failure);
            _error.ToString().Should().Contain("uncommitted changes");
        }

        Run("restore", repo.Info.WorkingDirectory, file, "--force").Should().Be(Cli.Success);
        repo.ReadFile("a.txt").Should().Be("1");
    }

    [Test]
    public void DiffListsChangesSinceSnapshot()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var file = GitTasks.GetTempPath() + ".json";
        repo.Snapshot().Save(file);
        repo.AddTag("v1");

        var exitCode = Run("diff", repo.Info.WorkingDirectory, file);

        using (new AssertionScope())
        {
            exitCode.Should().Be(Cli.Success);
            _output.ToString().Should().Contain("+ Tag v1");
        }
    }

    [Test]
    public void DiffWithoutChangesSaysSo()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();
        repo.CommitFile("a.txt", "1");
        var file = GitTasks.GetTempPath() + ".json";
        repo.Snapshot().Save(file);

        Run("diff", repo.Info.WorkingDirectory, file);

        _output.ToString().Should().Contain("No changes");
    }

    [Test]
    public void MissingRepositoryIsAnError()
    {
        var exitCode = Run("snapshot", GitTasks.GetTempPath());

        using (new AssertionScope())
        {
            exitCode.Should().Be(Cli.Failure);
            _error.ToString().Should().StartWith("error:");
        }
    }

    [Test]
    public void MissingSnapshotFileIsAnError()
    {
        using var repo = GitTasks.CreateTemporaryGitRepository();

        Run("restore", repo.Info.WorkingDirectory, GitTasks.GetTempPath() + ".json").Should().Be(Cli.Failure);
    }
}
