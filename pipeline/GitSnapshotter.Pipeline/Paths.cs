using ModularPipelines.Context;
using ModularPipelines.Git.Extensions;

namespace GitSnapshotter.Pipeline;

internal static class Paths
{
    public static string Root(IPipelineContext context) => context.Git().RootDirectory.Path;

    public static string Solution(IPipelineContext context) =>
        Path.Combine(Root(context), "Source", "GitSnapshotter.slnx");

    public static string Library(IPipelineContext context) =>
        Path.Combine(Root(context), "Source", "GitSnapshotter", "GitSnapshotter.csproj");

    public static string Console(IPipelineContext context) =>
        Path.Combine(Root(context), "Source", "GitSnapshotter.Console", "GitSnapshotter.Console.csproj");

    /// <summary>A project's intermediate directory (<c>obj</c>), where restore writes the assets file.</summary>
    public static string IntermediateDirectory(string project) => Path.Combine(Path.GetDirectoryName(project)!, "obj");

    /// <summary>A project's restore graph.</summary>
    public static string AssetsFile(string project) => Path.Combine(IntermediateDirectory(project), "project.assets.json");

    public static string Changelog(IPipelineContext context) => Path.Combine(Root(context), Pipeline.Changelog.FileName);

    public static string Docs(IPipelineContext context) => Path.Combine(Root(context), "docs");

    public static string SmokeTests(IPipelineContext context) =>
        Path.Combine(Root(context), "pipeline", "GitSnapshotter.SmokeTests", "GitSnapshotter.SmokeTests.csproj");

    public static string ReleaseDirectory(IPipelineContext context, BuildSettings settings) =>
        Path.GetFullPath(settings.ReleaseDirectory, Root(context));

    public static string ReleaseNotesDirectory(IPipelineContext context, BuildSettings settings) =>
        Path.GetFullPath(settings.ReleaseNotesDirectory, Root(context));

    public static string PublishDirectory(IPipelineContext context, BuildSettings settings) =>
        Path.GetFullPath(settings.PublishDirectory, Root(context));
}
