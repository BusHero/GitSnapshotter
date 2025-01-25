namespace GitSnapshotter;

public sealed record GitRepositorySnapshot
{
    public required string Head { get; init; }

    public string[] Branches { get; set; } = [];
}