﻿using LibGit2Sharp;

namespace GitSnapshotter;

public class GitRepository
{
    public static GitRepositorySnapshot GetSnapshot(string pathToRepository)
    {
        using var repo = new Repository(pathToRepository);

        var branches = repo.Branches.Select(x => x.FriendlyName).ToArray();
        
        return new GitRepositorySnapshot
        {
            Head = null!,
            Branches = branches,
        };
    }
}