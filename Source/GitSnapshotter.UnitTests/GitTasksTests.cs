using FluentAssertions;

namespace GitSnapshotter.UnitTests;

public class GitTasksTests
{
    [Fact]
    public async Task NewGitRepositoryIsGitRepository()
    {
        var directory = GetTempDirectoryName();
        
        await GitTasks.CreateGitRepository(directory);
        
        var result = await GitTasks.IsGitRepository(directory);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task MissingFolderIsNoGitRepository()
    {
        var directory = GetTempDirectoryName();

        var result = await GitTasks.IsGitRepository(directory);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task NewFolderIsNoGitRepository()
    {
        var directory = GetTempDirectoryName();
        
        Directory.CreateDirectory(directory);
        
        var result = await GitTasks.IsGitRepository(directory);

        result.Should().BeFalse();
    }
    
    [Fact]
    public async Task DeletedGitRepositoryIsNoGitRepository()
    {
        var directory = GetTempDirectoryName();
        
        await GitTasks.CreateGitRepository(directory);
        
        Directory.Delete(directory, true);
        
        var result = await GitTasks.IsGitRepository(directory);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CreateGitRepositoryIsIdempotent()
    {
        var directory = GetTempDirectoryName();
        
        await GitTasks.CreateGitRepository(directory);
        await GitTasks.CreateGitRepository(directory);
        
        var result = await GitTasks.IsGitRepository(directory);

        result.Should().BeTrue();
    }

    // [Fact]
    // public async Task GetHead()
    // {
    //     var tempGitDirectory = Path.Join(
    //         Path.GetTempPath(), 
    //         Guid.NewGuid().ToString("N"));
    //     
    //     await Cli.Wrap("git")
    //         .WithArguments(_ => _
    //             .Add("init")
    //             .Add(tempGitDirectory))
    //         .ExecuteAsync();
    //
    //     var tempFileName = Path.Combine(
    //         tempGitDirectory, 
    //         Guid.NewGuid().ToString("N"));
    //     
    //     await File.WriteAllTextAsync(tempFileName, "");
    //     
    //     await Cli.Wrap("git")
    //         .WithArguments(_ => _
    //             .Add(["-C", tempGitDirectory])
    //             .Add("add")
    //             .Add(tempFileName))
    //         .ExecuteAsync();
    //     
    //     await Cli.Wrap("git")
    //         .WithArguments(_ => _
    //             .Add(["-C", tempGitDirectory])
    //             .Add("commit")
    //             .Add(["-m", Guid.NewGuid().ToString("N")]))
    //         .WithStandardOutputPipe(PipeTarget.ToDelegate(Console.WriteLine))
    //         .WithStandardErrorPipe(PipeTarget.ToDelegate(Console.WriteLine))
    //         .ExecuteAsync();
    //     
    //     var result = GitRepository.GetSnapshot();
    // }

    private static string GetTempDirectoryName()
    {
        return Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
    }
}