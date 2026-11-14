namespace GitSnapshotter.Pipeline.Tests;

/// <summary>The repository the tests were built from.</summary>
internal static class Repository
{
    /// <summary>The repository root: the first directory above the test output that holds global.json.</summary>
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException($"No global.json above {AppContext.BaseDirectory}.");
    }
}
