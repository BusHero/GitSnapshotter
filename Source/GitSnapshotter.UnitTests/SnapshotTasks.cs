using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

internal static class SnapshotTasks
{
    public static GitRepositorySnapshot Snapshot(this Repository repository, SnapshotOptions? options = null) =>
        GitRepository.GetSnapshot(repository.Info.WorkingDirectory, options);

    public static GitRepositorySnapshot SnapshotWithWorkingTree(this Repository repository) =>
        repository.Snapshot(new SnapshotOptions { IncludeWorkingTree = true });

    public static void RestoreFrom(this Repository repository, GitRepositorySnapshot snapshot, RestoreOptions? options = null) =>
        GitRepository.Restore(repository.Info.WorkingDirectory, snapshot, options);
}
