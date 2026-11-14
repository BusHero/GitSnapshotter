using System.Diagnostics;
using System.Text;

namespace GitSnapshotter.SmokeTests;

/// <summary>What a command printed.</summary>
/// <param name="ExitCode">Its exit code.</param>
/// <param name="StandardOutput">What it wrote to standard output.</param>
/// <param name="StandardError">What it wrote to standard error.</param>
public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Runs the commands a user would: dotnet, git, the installed tool.</summary>
internal static class Command
{
    /// <summary>
    /// Runs <paramref name="file"/> and logs it, with what it printed, to the test's output.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// It exited with another code than 0, unless <paramref name="check"/> is false.
    /// </exception>
    public static async Task<CommandResult> RunAsync(
        string file,
        IEnumerable<string> arguments,
        string directory,
        IReadOnlyDictionary<string, string>? environment = null,
        bool check = true)
    {
        var start = new ProcessStartInfo(file)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            start.Environment[name] = value;
        }

        var line = $"{directory}> {file} {string.Join(' ', start.ArgumentList.Select(Quote))}";
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {file}.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var result = new CommandResult(process.ExitCode, await output, await error);

        var log = new StringBuilder(line).AppendLine();
        foreach (var text in new[] { result.StandardOutput, result.StandardError }.Where(text => text.Length > 0))
        {
            log.AppendLine(text.TrimEnd());
        }

        Console.WriteLine(log.ToString().TrimEnd());

        if (check && result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{line} exited with {result.ExitCode}:\n{result.StandardOutput}\n{result.StandardError}".TrimEnd());
        }

        return result;
    }

    private static string Quote(string argument) =>
        argument.Length > 0 && !argument.Any(char.IsWhiteSpace) ? argument : $"\"{argument}\"";
}
