using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitSnapshotter;

/// <summary>
/// The state of a git repository's references at a point in time: HEAD, local branches,
/// remotes, tags and any other references, plus (optionally) the index and working tree.
/// </summary>
/// <remarks>
/// A snapshot stores names and object ids only. Restoring it requires the referenced objects
/// to still be present in the repository's object database.
/// </remarks>
public sealed record GitRepositorySnapshot
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// The name of the checked-out branch, or the commit id when <see cref="IsHeadDetached"/> is <c>true</c>.
    /// </summary>
    public required string Head { get; init; }

    /// <summary>
    /// <c>true</c> when HEAD pointed directly at a commit rather than at a branch.
    /// </summary>
    public bool IsHeadDetached { get; init; }

    /// <summary>The local branches.</summary>
    public required Branch[] Branches { get; init; }

    /// <summary>The configured remotes and their remote-tracking branches.</summary>
    public required Remote[] Remotes { get; init; }

    /// <summary>The tags.</summary>
    public required Tag[] Tags { get; init; }

    /// <summary>
    /// Direct references outside <c>refs/heads</c>, <c>refs/tags</c> and <c>refs/remotes</c>
    /// (for example <c>refs/stash</c> or <c>refs/notes/commits</c>), keyed by canonical name.
    /// </summary>
    public Dictionary<string, string> References { get; init; } = [];

    /// <summary>
    /// The index and working tree, when the snapshot was taken with
    /// <see cref="SnapshotOptions.IncludeWorkingTree"/>; otherwise <c>null</c>.
    /// </summary>
    public WorkingTreeState? WorkingTree { get; init; }

    /// <summary>Serializes the snapshot to indented JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Deserializes a snapshot produced by <see cref="ToJson"/>.</summary>
    /// <exception cref="JsonException">The text is not a valid snapshot.</exception>
    public static GitRepositorySnapshot FromJson(string json) =>
        JsonSerializer.Deserialize<GitRepositorySnapshot>(json, JsonOptions)
        ?? throw new JsonException("The JSON document does not contain a snapshot.");

    /// <summary>Writes the snapshot as JSON to <paramref name="path"/>, overwriting any existing file.</summary>
    public void Save(string path) => File.WriteAllText(path, ToJson());

    /// <summary>Reads a snapshot previously written with <see cref="Save"/>.</summary>
    public static GitRepositorySnapshot Load(string path) => FromJson(File.ReadAllText(path));
}

/// <summary>A local branch.</summary>
public sealed record Branch
{
    /// <summary>The short name, e.g. <c>main</c> or <c>feature/x</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The id of the commit the branch points at.</summary>
    public required string Tip { get; init; }

    /// <summary>The short name of the remote-tracking branch this branch follows, e.g. <c>origin/main</c>.</summary>
    public string? TrackedBranch { get; init; }

    /// <summary>The configured remote (<c>branch.&lt;name&gt;.remote</c>).</summary>
    public string? RemoteName { get; init; }

    /// <summary>The configured upstream branch on the remote (<c>branch.&lt;name&gt;.merge</c>), e.g. <c>refs/heads/main</c>.</summary>
    public string? UpstreamBranch { get; init; }

    /// <summary>Deconstructs the branch into its name and tip.</summary>
    public void Deconstruct(out string name, out string tip)
    {
        name = Name;
        tip = Tip;
    }
}

/// <summary>A configured remote.</summary>
public sealed record Remote
{
    /// <summary>
    /// Remote-tracking branches, keyed by short name (e.g. <c>origin/main</c>), with the commit id each points at.
    /// Symbolic references such as <c>origin/HEAD</c> are recorded in <see cref="Head"/> instead.
    /// </summary>
    public required Dictionary<string, string> Branches { get; init; }

    /// <summary>The fetch URL.</summary>
    public required string Url { get; init; }

    /// <summary>The remote's name, e.g. <c>origin</c>.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The short name of the branch <c>refs/remotes/&lt;remote&gt;/HEAD</c> points at (e.g. <c>origin/main</c>), if set.
    /// </summary>
    public string? Head { get; init; }
}

/// <summary>A tag.</summary>
public sealed record Tag
{
    /// <summary>The tag's name.</summary>
    public required string Name { get; init; }

    /// <summary>The id of the object the tag points at (for annotated tags, the annotation's target).</summary>
    public required string Target { get; init; }

    /// <summary>The annotation message, or <c>null</c> for a lightweight tag.</summary>
    public string? Message { get; init; }

    /// <summary>
    /// The id of the annotation object for an annotated tag, or <c>null</c> for a lightweight tag.
    /// When it is still in the object database, restore reuses it so the tag comes back unchanged.
    /// </summary>
    public string? AnnotationSha { get; init; }

    /// <summary>Deconstructs the tag into its name and target.</summary>
    public void Deconstruct(out string name, out string target)
    {
        name = Name;
        target = Target;
    }

    /// <summary>Deconstructs the tag into its name, target and message.</summary>
    public void Deconstruct(out string name, out string target, out string? message)
    {
        name = Name;
        target = Target;
        message = Message;
    }
}

/// <summary>The index and working tree, stored as tree objects in the repository's object database.</summary>
/// <remarks>
/// The trees are not referenced by any ref, so <c>git gc</c> may eventually prune them
/// (by default after two weeks).
/// </remarks>
public sealed record WorkingTreeState
{
    /// <summary>The id of the tree written from the index (staged content).</summary>
    public required string IndexTree { get; init; }

    /// <summary>The id of the tree holding the working directory's tracked and untracked (non-ignored) files.</summary>
    public required string WorkingDirectoryTree { get; init; }
}
