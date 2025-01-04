using CliWrap;

namespace GitSnapshotter.UnitTests;

public static class GitTasks
{
    public static Command CreateGitRepositoryCommand(string path)
        => Cli.Wrap("git")
            .WithArguments(_ => _
                .Add("init")
                .Add(path));


    public static async Task CreateGitRepository(string path)
    {
        await CreateGitRepositoryCommand(path) 
            .ExecuteAsync();
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