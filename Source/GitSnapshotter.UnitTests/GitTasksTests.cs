using FluentAssertions;

namespace GitSnapshotter.UnitTests;

public class GitTasksTests
{
    [Fact]
    public async Task NewGitRepositoryIsGitRepository()
    {
        var gitRepository = await GitTasks.CreateGitRepository();

        var result = await GitTasks.IsGitRepository(gitRepository);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task MissingFolderIsNoGitRepository()
    {
        var gitRepository = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var result = await GitTasks.IsGitRepository(gitRepository);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task NewFolderIsNoGitRepository()
    {
        var gitRepository = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(gitRepository);
        
        var result = await GitTasks.IsGitRepository(gitRepository);

        result.Should().BeFalse();
    }
    
    [Fact]
    public async Task DeletedGitRepositoryIsNoGitRepository()
    {
        var gitRepository = await GitTasks.CreateGitRepository();
        
        Directory.Delete(gitRepository, true);
        
        var result = await GitTasks.IsGitRepository(gitRepository);

        result.Should().BeFalse();
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
}