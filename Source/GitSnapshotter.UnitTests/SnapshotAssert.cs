namespace GitSnapshotter.UnitTests;

internal static class SnapshotAssert
{
    /// <summary>
    /// Asserts that two snapshots are equivalent, comparing remote URLs exactly as git has them.
    /// </summary>
    /// <remarks>
    /// <c>IsEquivalentTo</c> compares member by member, so on its own it would read every property of each
    /// <see cref="Remote.Url"/>. Properties such as <see cref="Uri.AbsolutePath"/> throw for a relative
    /// <see cref="Uri"/>, which is what local paths (on Linux and macOS) and scp-style URLs (<c>git@host:path</c>)
    /// become. So the structural comparison skips <see cref="Uri"/> members, and the URLs are compared by
    /// <see cref="Uri.OriginalString"/> instead.
    /// </remarks>
    public static async Task AreEquivalent(GitRepositorySnapshot actual, GitRepositorySnapshot expected)
    {
        await Assert.That(actual).IsEquivalentTo(expected).IgnoringType<Uri>();
        await Assert.That(RemoteUrls(actual)).IsEquivalentTo(RemoteUrls(expected), CollectionOrdering.Matching);
    }

    private static List<string> RemoteUrls(GitRepositorySnapshot snapshot) =>
        [.. snapshot.Remotes.Select(r => $"{r.Name} {r.Url.OriginalString}")];
}
