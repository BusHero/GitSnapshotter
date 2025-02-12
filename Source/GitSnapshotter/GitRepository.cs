using LibGit2Sharp;

namespace GitSnapshotter;

public sealed class GitRepository
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
        var originalBranches = repository
            .Branches
            .Select(x => x.FriendlyName)
            .ToArray();
        var snapshotBranches = snapshot
            .Branches
            .Select(x => x.Name)
            .ToArray();

        RestoreBranches(snapshot.Branches, originalBranches, repository);
        RemoveBranches(originalBranches, snapshotBranches, repository);
        RestoreTips(snapshot.Branches, repository);
    }

    private static void RestoreBranches(
        IEnumerable<Branch> branches,
        IEnumerable<string> originalBranches,
        Repository repository)
    {
        foreach (var (name, tip) in branches.ExceptBy(originalBranches, x => x.Name))
        {
            repository.CreateBranch(name, tip);
        }
    }

    private static void RestoreTips(IEnumerable<Branch> branches, Repository repository)
    {
        foreach (var (name, tip) in branches)
        {
            repository.Refs.UpdateTarget(repository.Branches[name].Reference, tip);
        }
    }

    private static void RemoveBranches(
        IEnumerable<string> originalBranches,
        IEnumerable<string> snapshotBranches,
        Repository repository)
    {
        foreach (var branchToDelete in originalBranches.Except(snapshotBranches))
        {
            repository.Branches.Remove(branchToDelete);
        }
    }

    private static void CreateBranches(
        GitRepositorySnapshot snapshot,
        string[] originalBranches,
        Repository repository)
    {
        var branchesToAdd = snapshot
            .Branches
            .ExceptBy(originalBranches, x => x.Name);

        foreach (var (name, tip) in branchesToAdd)
        {
            repository.CreateBranch(name, tip);
        }
    }
}