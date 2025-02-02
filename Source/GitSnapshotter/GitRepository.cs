using LibGit2Sharp;

namespace GitSnapshotter;

public class GitRepository
{
    public static GitRepositorySnapshot GetSnapshot(string pathToRepository)
    {
        using var repo = new Repository(pathToRepository);

        var head = repo.Head.FriendlyName;
        var branches = repo.Branches
            .ToDictionary(x => x.FriendlyName, x => x.Tip.Sha);
        var remotes = repo
            .Network
            .Remotes
            .ToDictionary(x => x.Name, x => x.Url);

        return new GitRepositorySnapshot
        {
            Head = head,
            Branches = branches,
            Remotes = remotes,
        };
    }
}