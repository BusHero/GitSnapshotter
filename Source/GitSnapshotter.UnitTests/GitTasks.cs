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

    /// <summary>Creates a repository with one commit, cloned from a new temporary repository, so it has an <c>origin</c> remote.</summary>
    public static (Repository Clone, Repository Origin) CreateClonedRepository()
    {
        var origin = CreateTemporaryGitRepository();
        origin.AddFileToRepository();
        origin.CommitChanges();

        var path = GetTempPath();
        Repository.Clone(origin.Info.Path, path);

        return (new Repository(path), origin);
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

    /// <summary>Writes a file in the working directory without staging it.</summary>
    public static void WriteFile(this Repository repository, string relativePath, string content)
    {
        var fullPath = repository.FullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    public static string ReadFile(this Repository repository, string relativePath) =>
        File.ReadAllText(repository.FullPath(relativePath));

    public static bool FileExists(this Repository repository, string relativePath) =>
        File.Exists(repository.FullPath(relativePath));

    public static void DeleteFile(this Repository repository, string relativePath) =>
        File.Delete(repository.FullPath(relativePath));

    public static string FullPath(this Repository repository, string relativePath) =>
        Path.Combine(repository.Info.WorkingDirectory, relativePath);

    /// <summary>Writes, stages and commits a file.</summary>
    public static Commit CommitFile(this Repository repository, string relativePath, string content)
    {
        repository.WriteFile(relativePath, content);
        repository.Index.Add(relativePath);
        repository.Index.Write();
        repository.CommitChanges();

        return repository.Head.Tip;
    }

    public static void StageFile(this Repository repository, string relativePath)
    {
        repository.Index.Add(relativePath);
        repository.Index.Write();
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

    public static void Fetch(this Repository repository, string remote)
    {
        Commands.Fetch(repository, remote, [], new FetchOptions(), null);
    }

    public static void AddTag(this Repository repository, string tag)
    {
        repository.Tags.Add(tag, repository.Head.Tip);
    }

    public static void AddAnnotatedTag(this Repository repository, string tag, string message)
    {
        var signature = repository.Config.BuildSignature(DateTimeOffset.Now);
        repository.Tags.Add(tag, repository.Head.Tip, signature, message);
    }

    public static void MoveTag(this Repository repository, string tag, Commit target)
    {
        repository.Refs.UpdateTarget(repository.Tags[tag].Reference, target.Id);
    }

    /// <summary><c>true</c> when the index and working directory match HEAD (untracked files allowed).</summary>
    public static bool IsClean(this Repository repository) =>
        !repository.RetrieveStatus(new StatusOptions { IncludeUntracked = false }).IsDirty;

    /// <summary>Reopens the repository, so index and refs are read fresh from disk.</summary>
    public static Repository Reopen(this Repository repository) => new(repository.Info.WorkingDirectory);
}
