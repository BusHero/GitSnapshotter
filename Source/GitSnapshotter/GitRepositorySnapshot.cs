namespace GitSnapshotter;

public sealed record GitRepositorySnapshot
{
    public required string Head { get; init; }

    public required Branch[] Branches { get; init; }

    public required Remote[] Remotes { get; init; }

    public required Tag[] Tags { get; init; }
}

public record Branch
{
    public required string Name { get; init; }
    
    public required string Tip { get; init; }

    public string? TrackedBranch { get; init; }

    public string? RemoteName { get; init; }

    public void Deconstruct(out string name, out string tip)
    {
        name = Name;
        tip = Tip;
    }
}

public sealed record Remote
{
    public required Dictionary<string, string> Branches { get; init; }

    public required string Url { get; init; }

    public required string Name { get; init; }
}

public sealed record Tag
{
    public required string Target { get; init; }

    public string? Message { get; init; }

    public required string Name { get; init; }
}