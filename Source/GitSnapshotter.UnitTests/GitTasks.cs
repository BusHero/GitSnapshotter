using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

internal static class GitTasks
{
    public static string CreateTemporaryGitRepository()
    {
        var path = GetTempDirectoryName();

        Repository.Init(path);

        return path;
    }

    private static string GetTempDirectoryName()
    {
        return Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
    }

    public static string AddFileToRepository(string pathToRepo)
    {
        var filename = Guid.NewGuid().ToString("N");

        File.WriteAllText(Path.Combine(pathToRepo, filename), filename);

        var repository = new Repository(pathToRepo);
        repository.Index.Add(filename);
        repository.Index.Write();

        return filename;
    }

    public static void AddBranch(string pathToRepo, string branch)
    {
        using var repo = new Repository(pathToRepo);
        repo.CreateBranch(branch);
    }

    public static void CommitChanges(string pathToRepo)
    {
        using var repo = new Repository(pathToRepo);
        var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
        var commitMessage = $"Committing {pathToRepo}";

        repo.Commit(commitMessage, signature, signature);
    }

    public static void CheckoutBranch(string pathToRepository, string branchName)
    {
        using var repo = new Repository(pathToRepository);

        Commands.Checkout(repo, branchName);
    }
}