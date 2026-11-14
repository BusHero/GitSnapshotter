namespace GitSnapshotter.Pipeline;

/// <summary>Talks to the GitHub Actions runner the pipeline runs on, if any.</summary>
internal static class GitHubActions
{
    /// <summary>
    /// Sets an output of the current step (<c>steps.&lt;id&gt;.outputs.&lt;name&gt;</c>). Does nothing outside GitHub
    /// Actions.
    /// </summary>
    public static async Task SetOutputAsync(string name, string value, CancellationToken cancellationToken)
    {
        if (value.Contains('\n', StringComparison.Ordinal))
        {
            throw new ArgumentException("Step outputs written by the pipeline are single lines.", nameof(value));
        }

        var outputFile = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
        if (!string.IsNullOrEmpty(outputFile))
        {
            await File.AppendAllTextAsync(outputFile, $"{name}={value}\n", cancellationToken);
        }
    }
}
