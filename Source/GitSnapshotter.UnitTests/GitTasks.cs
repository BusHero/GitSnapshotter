using CliWrap;

namespace GitSnapshotter.UnitTests;

public static class GitTasks
{
    public static async Task CreateGitRepository(string path)
    {
        await Cli.Wrap("git")
            .WithArguments(_ => _
                .Add("init")
                .Add(["--initial-branch", "main"])
                .Add(path))
            .WithValidation(CommandResultValidation.None)
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
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync();

            return result.IsSuccess;
        }
        catch
        {
            return false;
        }
    }
}