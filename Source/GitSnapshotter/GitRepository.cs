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

        RestoreBranches(snapshot, repository);
        RestoreTags(snapshot, repository);

        Commands.Checkout(repository, snapshot.Head);
    }

    private static void RestoreTags(
        GitRepositorySnapshot snapshot,
        Repository repository)
    {
        var originalTags = repository
            .Tags
            .Select(x => x.FriendlyName)
            .ToArray();
        var snapshotTags = snapshot
            .Tags
            .Select(x => x.Name)
            .ToArray();

        var tags = snapshot.Tags;

        AddRemovedTag(tags, originalTags, repository);
        RemoveAddedTag(originalTags, snapshotTags, repository);
        RestoreTagReferences(snapshot, repository);
    }

    private static void RestoreTagReferences(
        GitRepositorySnapshot snapshot,
        Repository repository)
    {
        var originalTags = repository
            .Tags
            .ToDictionary(x => x.FriendlyName, x => x.Target.Sha);

        if (snapshot.Tags is [])
        {
            return;
        }

        if (originalTags.Count == 0)
        {
            return;
        }

        foreach (var tag in snapshot.Tags)
        {
            if (!originalTags.TryGetValue(tag.Name, out var value))
            {
                break;
            }

            if (value == tag.Target)
            {
                break;
            }

            repository.Tags.Remove(snapshot.Tags[0].Name);

            if (snapshot.Tags[0].Message != null)
            {
                repository.Tags.Add(
                    snapshot.Tags[0].Name,
                    snapshot.Tags[0].Target,
                    repository.Config.BuildSignature(DateTimeOffset.Now),
                    snapshot.Tags[0].Message);
            }
            else
            {
                repository.Tags.Add(snapshot.Tags[0].Name, snapshot.Tags[0].Target);
            }
        }
    }

    private static void RemoveAddedTag(
        IEnumerable<string> originalTags,
        IEnumerable<string> snapshotTags,
        Repository repository)
    {
        foreach (var tag in originalTags.Except(snapshotTags))
        {
            repository.Tags.Remove(tag);
        }
    }

    private static void AddRemovedTag(
        IEnumerable<Tag> tags,
        IEnumerable<string> originalTags,
        Repository repository)
    {
        var signature = repository.Config.BuildSignature(DateTimeOffset.Now);

        foreach (var (name, target, message) in tags.ExceptBy(originalTags, x => x.Name))
        {
            if (message is null)
            {
                repository.Tags.Add(name, target);
            }
            else
            {
                repository.Tags.Add(name, target, signature, message);
            }
        }
    }

    private static void RestoreBranches(
        GitRepositorySnapshot snapshot,
        Repository repository)
    {
        var originalBranches = repository
            .Branches
            .Select(x => x.FriendlyName)
            .ToArray();
        var snapshotBranches = snapshot
            .Branches
            .Select(x => x.Name)
            .ToArray();

        AddRemovedBranches(snapshot.Branches, originalBranches, repository);
        RemoveAddedBranches(originalBranches, snapshotBranches, repository);
        RestoreTips(snapshot.Branches, repository);
    }

    private static void RestoreTips(
        IEnumerable<Branch> branches,
        Repository repository)
    {
        foreach (var (name, tip) in branches)
        {
            repository.Refs.UpdateTarget(repository.Branches[name].Reference, tip);
        }
    }

    private static void RemoveAddedBranches(
        IEnumerable<string> originalBranches,
        IEnumerable<string> snapshotBranches,
        Repository repository)
    {
        foreach (var branchToDelete in originalBranches.Except(snapshotBranches))
        {
            repository.Branches.Remove(branchToDelete);
        }
    }

    private static void AddRemovedBranches(
        IEnumerable<Branch> branches,
        IEnumerable<string> originalBranches,
        Repository repository)
    {
        foreach (var (name, tip) in branches.ExceptBy(originalBranches, x => x.Name))
        {
            repository.CreateBranch(name, tip);
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