using FluentAssertions;

namespace GitSnapshotter.UnitTests;

public class CreateGitRepositoryTests
{
    [Fact]
    public async Task NewGitRepositoryIsGitRepository()
    {
        var directory = await CreateTemporaryGitRepositoryAsync();
        
        var result = await GitTasks.IsGitRepositoryAsync(directory);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task MissingFolderIsNoGitRepository()
    {
        var directory = GetTempDirectoryName();

        var result = await GitTasks.IsGitRepositoryAsync(directory);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task NewFolderIsNoGitRepository()
    {
        var directory = GetTempDirectoryName();
        
        Directory.CreateDirectory(directory);
        
        var result = await GitTasks.IsGitRepositoryAsync(directory);

        result.Should().BeFalse();
    }
    
    [Fact]
    public async Task DeletedGitRepositoryIsNoGitRepository()
    {
        var directory = await CreateTemporaryGitRepositoryAsync();
        
        Directory.Delete(directory, true);
        
        var result = await GitTasks.IsGitRepositoryAsync(directory);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CreateGitRepositoryIsIdempotent()
    {
        var directory = await CreateTemporaryGitRepositoryAsync();
        
        await GitTasks.CreateGitRepositoryAsync(directory);
        
        var result = await GitTasks.IsGitRepositoryAsync(directory);

        result.Should().BeTrue();
    }

    private static async Task<string> CreateTemporaryGitRepositoryAsync()
    {
        var directory = GetTempDirectoryName();
        
        await GitTasks.CreateGitRepositoryAsync(directory);
        
        return directory;
    }
    
    private static string GetTempDirectoryName()
    {
        return Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
    }
}