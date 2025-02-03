using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

internal static class GitTasks
{
    public static Repository CreateTemporaryGitRepository()
    {
        var path = GetTempPath();

        Repository.Init(path);

        return new Repository(path);
    }

    public static string GetTempPath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
    }

    public static string AddFileToRepository(this Repository repository)
    {
        var filename = Guid.NewGuid().ToString("N");

        File.WriteAllText(Path.Combine(repository.Info.WorkingDirectory, filename), filename);

        repository.Index.Add(filename);
        repository.Index.Write();

        return filename;
    }

    public static void AddBranch(this Repository repo, string branch)
    {
        repo.CreateBranch(branch);
    }

    public static void CommitChanges(this Repository repo)
    {
        var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
        repo.Commit($"Committing new changes", signature, signature);
    }

    public static void CheckoutBranch(this Repository repo, string branchName)
    {
        Commands.Checkout(repo, branchName);
    }

    public static bool IsValidCommit(this Repository repository, string sha)
    {
        return repository.Lookup<Commit>(sha) != null;
    }

    public static void AddRemote(this Repository repository, string remote, string url)
    {
        repository.Network.Remotes.Add(remote, url);
    }

    public static void AddTag(this Repository repository, string tag)
    {
    }
}