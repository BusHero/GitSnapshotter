using CliWrap;

namespace GitSnapshotter.UnitTests;

public static class GitTasks
{
    public static async Task<string> CreateGitRepository(string path)
    {
        await Cli.Wrap("git")
            .WithArguments(_ => _
                .Add("init")
                .Add(path))
            .ExecuteAsync();

        return path;
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