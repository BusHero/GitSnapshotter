using CliWrap;

namespace GitSnapshotter.UnitTests;

public static class GitTasks
{
    public static async Task<string> CreateGitRepository()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));

        await Cli.Wrap("git")
            .WithArguments(_ => _
                .Add("init")
                .Add(directory))
            .ExecuteAsync();

        return directory;
    }

    public static async Task<bool> IsGitRepository(string path)
    {
        try
        {
            var result = await Cli.Wrap("git")
                .WithArguments(_ => _
                    .Add(["-C", path])
                    .Add("status"))
                .ExecuteAsync();
        
            return result.IsSuccess;
        }
        catch
        {
            return false;
        }
    }
}