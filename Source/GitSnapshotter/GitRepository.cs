using LibGit2Sharp;

namespace GitSnapshotter;

public class GitRepository
{
    public static GitRepositorySnapshot GetSnapshot(string pathToRepository)
    {
        using var repo = new Repository(pathToRepository);

        var head = repo.Head.FriendlyName;
        var branches = repo.Branches
            .Where(b => !b.IsRemote)
            .Select(branch => new Branch
            {
                Tip = branch.Tip.Sha,
                TrackedBranch = branch.TrackedBranch?.FriendlyName,
                RemoteName = branch.RemoteName,
                Name = branch.FriendlyName,
            })
            .ToArray();

        var remotes = repo
            .Network
            .Remotes
            .Select(remote =>
            {
                var array = repo.Branches
                    .Where(branch => branch.IsRemote)
                    .Where(branch => branch.RemoteName.Equals(remote.Name))
                    .ToDictionary(x => x.FriendlyName, x => x.Tip.Sha);

                return new Remote
                {
                    Url = remote.Url,
                    Branches = array,
                    Name = remote.Name,
                };
            })
            .ToArray();

        var tags = repo
            .Tags
            .Select(tag => new Tag
            {
                Target = tag.Target.Sha,
                Message = tag.Annotation?.Message,
                Name = tag.FriendlyName,
            })
            .ToArray();

        return new GitRepositorySnapshot
        {
            Head = head,
            Branches = branches,
            Remotes = remotes,
            Tags = tags,
        };
    }

    public static void Restore(
        string pathToRepository,
        GitRepositorySnapshot snapshot)
    {
        using var repository = new Repository(pathToRepository);
        var originalBranches = repository.Branches.Select(x => x.FriendlyName).ToArray();

        var removedBranches = snapshot
            .Branches
            .Where(x => !originalBranches.Contains(x.Name));

        foreach (var branch in removedBranches)
        {
            repository.CreateBranch(branch.Name, branch.Tip);
        }
    }
}