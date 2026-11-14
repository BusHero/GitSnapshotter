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
    /// <summary>How long a command may run before it is killed: none of them should take minutes.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long to keep reading a command's output after it exited. A process it started and left running (an MSBuild
    /// node, a compiler server) may hold the output open long after; what the command itself printed is in by then.
    /// </summary>
    private static readonly TimeSpan OutputGrace = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Runs <paramref name="file"/> and logs it, with what it printed, to the test's output.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// It exited with another code than 0 (unless <paramref name="check"/> is false), or ran longer than
    /// <see cref="Timeout"/> and was killed.
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

        // Nothing to answer: a prompt gets end of input instead of waiting for a person.
        process.StandardInput.Close();

        var output = new StringBuilder();
        var error = new StringBuilder();
        var reading = Task.WhenAll(CopyAsync(process.StandardOutput, output), CopyAsync(process.StandardError, error));

        using var timeout = new CancellationTokenSource(Timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }

        try
        {
            await reading.WaitAsync(OutputGrace);
        }
        catch (TimeoutException)
        {
            // Held open by a process it left running; what it printed itself is in.
        }

        CommandResult result;
        lock (output)
        {
            lock (error)
            {
                result = new CommandResult(timedOut ? -1 : process.ExitCode, output.ToString(), error.ToString());
            }
        }

        var log = new StringBuilder(line).AppendLine();
        foreach (var text in new[] { result.StandardOutput, result.StandardError }.Where(text => text.Length > 0))
        {
            log.AppendLine(text.TrimEnd());
        }

        Console.WriteLine(log.ToString().TrimEnd());

        if (timedOut)
        {
            throw new InvalidOperationException(
                $"{line} ran longer than {Timeout} and was killed. What it printed:\n{result.StandardOutput}\n{result.StandardError}".TrimEnd());
        }

        if (check && result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{line} exited with {result.ExitCode}:\n{result.StandardOutput}\n{result.StandardError}".TrimEnd());
        }

        return result;
    }

    // Reads a stream into a buffer as it comes, so what was read is there even if the stream never ends.
    private static async Task CopyAsync(StreamReader reader, StringBuilder buffer)
    {
        var chunk = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(chunk)) > 0)
        {
            lock (buffer)
            {
                buffer.Append(chunk, 0, read);
            }
        }
    }

    private static string Quote(string argument) =>
        argument.Length > 0 && !argument.Any(char.IsWhiteSpace) ? argument : $"\"{argument}\"";
}
