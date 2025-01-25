using CliWrap;

namespace GitSnapshotter.UnitTests;

public static class GitTasks
{
    public static async Task CreateGitRepositoryAsync(string path)
    {
        await Cli.Wrap("git")
            .WithArguments(_ => _
                .Add("init")
                .Add(["--initial-branch", "main"])
                .Add(path))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(Console.Error.WriteLine))
            .WithStandardOutputPipe(PipeTarget.ToDelegate(Console.WriteLine))
            .ExecuteAsync();
    }

    public static async Task<bool> IsGitRepositoryAsync(string path)
    {
        try
        {
            var result = await Cli.Wrap("git")
                .WithArguments(_ => _
                    .Add(["-C", path])
                    .Add("status"))
                .WithStandardErrorPipe(PipeTarget.ToDelegate(Console.Error.WriteLine))
                .WithStandardOutputPipe(PipeTarget.ToDelegate(Console.WriteLine))
                .ExecuteAsync();

            return result.IsSuccess;
        }
        catch
        {
            return false;
        }
    }
}