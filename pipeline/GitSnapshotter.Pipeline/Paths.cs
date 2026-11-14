using ModularPipelines.Context;
using ModularPipelines.Git.Extensions;

namespace GitSnapshotter.Pipeline;

internal static class Paths
{
    public static string Root(IPipelineContext context) => context.Git().RootDirectory.Path;

    public static string Solution(IPipelineContext context) =>
        Path.Combine(Root(context), "Source", "GitSnapshotter.sln");

    public static string Library(IPipelineContext context) =>
        Path.Combine(Root(context), "Source", "GitSnapshotter", "GitSnapshotter.csproj");

    public static string Console(IPipelineContext context) =>
        Path.Combine(Root(context), "Source", "GitSnapshotter.Console", "GitSnapshotter.Console.csproj");

    public static string PublishDirectory(IPipelineContext context, BuildSettings settings) =>
        Path.GetFullPath(settings.PublishDirectory, Root(context));
}
