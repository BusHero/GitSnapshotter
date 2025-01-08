namespace GitSnapshotter;

public class GitRepository
{
    public static GitRepositorySnapshot GetSnapshot()
    {
        return new GitRepositorySnapshot
        {
            Head = null!,
        };
    }
}