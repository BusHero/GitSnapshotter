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
            .ToDictionary(
                branch => branch.FriendlyName,
                branch => new Branch
                {
                    Tip = branch.Tip.Sha,
                    IsTracking = branch.IsTracking,
                    TrackedBranch = branch.TrackedBranch?.FriendlyName,
                    RemoteName = branch.RemoteName,
                });

        var remotes = repo
            .Network
            .Remotes
            .ToDictionary(
                remote => remote.Name,
                remote =>
                {
                    var array = repo.Branches
                        .Where(branch => branch.IsRemote)
                        .Where(branch => branch.RemoteName.Equals(remote.Name))
                        .ToDictionary(x => x.FriendlyName, x => x.Tip.Sha);

                    return new Remote
                    {
                        Url = remote.Url,
                        Branches = array,
                    };
                });

        var tags = repo
            .Tags
            .ToDictionary(
                tag => tag.FriendlyName,
                tag => new Tag
                {
                    Target = tag.Target.Sha,
                    Message = tag.Annotation?.Message,
                });

        return new GitRepositorySnapshot
        {
            Head = head,
            Branches = branches,
            Remotes = remotes,
            Tags = tags,
        };
    }
}