namespace GitSnapshotter;

public sealed record GitRepositorySnapshot
{
    public required string Head { get; init; }

    public required Dictionary<string, Branch> Branches { get; init; }

    public required Dictionary<string, Remote> Remotes { get; init; }
}

public record Branch
{
    public required bool IsTracking { get; init; }

    public required string Tip { get; init; }

    public string? TrackedBranch { get; init; }

    public string? RemoteName { get; init; }
}

public sealed record Remote
{
    public required Dictionary<string, string> Branches { get; init; }

    public required string Url { get; init; }
}