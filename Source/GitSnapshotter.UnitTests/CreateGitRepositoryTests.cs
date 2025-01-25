﻿using FluentAssertions;
using FluentAssertions.Execution;

using LibGit2Sharp;

namespace GitSnapshotter.UnitTests;

public class CreateGitRepositoryTests
{
    [Fact]
    public void SnapshotOfInitialRepositoryContainsDefaultBranch()
    {
        var pathToRepository = CreateTemporaryGitRepository();
        AddFileToRepository(pathToRepository);
        CommitChanges(pathToRepository);
        
        var snapshot = GitRepository.GetSnapshot(pathToRepository);

        snapshot.Branches.Should().Contain("master");
    }
    
    [Theory, AutoData]
    public void SnapshotContainsAllCreatedBranches(string branchName)
    {
        var pathToRepository = CreateTemporaryGitRepository();
        AddFileToRepository(pathToRepository);
        CommitChanges(pathToRepository);
        AddBranch(pathToRepository, branchName);
        
        var snapshot = GitRepository.GetSnapshot(pathToRepository);
        
        snapshot.Branches.Should().Contain(branchName);
    }
    
    [Theory, AutoData]
    public void HeadContainsMaster(string branchName)
    {
        var pathToRepository = CreateTemporaryGitRepository();
        AddFileToRepository(pathToRepository);
        CommitChanges(pathToRepository);
        AddBranch(pathToRepository, branchName);
        
        var snapshot = GitRepository.GetSnapshot(pathToRepository);
        
        snapshot.Head.Should().Be("master");
    }
    
    [Theory, AutoData]
    public void HeadContainsCheckoutBranch(string branchName)
    {
        var pathToRepository = CreateTemporaryGitRepository();
        AddFileToRepository(pathToRepository);
        CommitChanges(pathToRepository);
        AddBranch(pathToRepository, branchName);
        CheckoutBranch(pathToRepository, branchName);
        
        var snapshot = GitRepository.GetSnapshot(pathToRepository);
        
        snapshot.Head.Should().Be(branchName);
    }

    private void CheckoutBranch(string pathToRepository, string branchName)
    {
        using var repo = new Repository(pathToRepository);
        
        Commands.Checkout(repo, branchName);
    }

    [Fact]
    public void CreateTemporaryGitRepositoryReturnsTemporaryGitRepository()
    {
        var pathToRepo = CreateTemporaryGitRepository();

        using (new AssertionScope())
        {
            Repository.IsValid(pathToRepo).Should().BeTrue();
            pathToRepo.Should().StartWith(Path.GetTempPath());
            pathToRepo.Should().NotEndWith(".git");
        }
    }
    
    [Fact]
    public void AddFileToRepositoryAddsFileToRepository()
    {
        var pathToRepo = CreateTemporaryGitRepository();
        
        var filename = AddFileToRepository(pathToRepo);
        
        File.Exists(Path.Combine(pathToRepo, filename)).Should().BeTrue();

        using var repo = new Repository(pathToRepo);
        repo.Index.Index().Select(x => x.Item.Path).Should().Contain(filename);
    }
    
    [Fact]
    public void CommitCommitsTest()
    {
        var pathToRepo = CreateTemporaryGitRepository();
        
        var filename = AddFileToRepository(pathToRepo);

        CommitChanges(pathToRepo);
        
        using var repo = new Repository(pathToRepo);
        
        repo.Commits.Count().Should().Be(1);
    }
    
    [Theory, AutoData]
    public void AddBranchAddsABranchTest(string branch)
    {
        var pathToRepo = CreateTemporaryGitRepository();
        
        var filename = AddFileToRepository(pathToRepo);

        CommitChanges(pathToRepo);
        AddBranch(pathToRepo, branch);
        
        using var repo = new Repository(pathToRepo);
        repo.Branches.Select(x => x.FriendlyName).Should().Contain(branch);
    }

    private void AddBranch(string pathToRepo, string branch)
    {
        using var repo = new Repository(pathToRepo);
        repo.CreateBranch(branch);
    }

    private void CommitChanges(string pathToRepo)
    {
        using var repo = new Repository(pathToRepo);
        var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
        var commitMessage = $"Committing {pathToRepo}";
        
        var commit = repo.Commit(commitMessage, signature, signature);
    }

    private static string AddFileToRepository(string pathToRepo)
    {
        var filename = Guid.NewGuid().ToString("N");

        File.WriteAllText(Path.Combine(pathToRepo, filename), filename);
        
        var repository = new Repository(pathToRepo);
        repository.Index.Add(filename);
        repository.Index.Write();
        
        return filename;
    }

    private static string CreateTemporaryGitRepository()
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
}