namespace GitSnapshotter;

public sealed record GitRepositorySnapshot
{
    public required string Head { get; init; }

    public required Dictionary<string, string> Branches { get; init; }

    public required Dictionary<string, string> Remotes { get; init; }
}