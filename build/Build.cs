using System;
using System.IO;
using System.Threading.Tasks;

using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;

using static Nuke.Common.Tools.DotNet.DotNetTasks;

[GitHubActions(
    "build",
    GitHubActionsImage.UbuntuLatest,
    On = [GitHubActionsTrigger.Push],
    AutoGenerate = false,
    InvokedTargets = [nameof(Compile)])]
sealed class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution(relativePath: "Source/GitSnapshotter.sln", GenerateProjects = true)]
    readonly Solution Solution = null!;

    [Parameter("Github Output")] private readonly string GithubOutput = null!;

    [Parameter] public AbsolutePath PublishDirectory { get; } = null!;

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            DotNetClean(_ => _
                .SetProject(Solution.GitSnapshotter_Console));
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(_ => _
                .SetProjectFile(Solution.GitSnapshotter_Console));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(_ => _
                .SetConfiguration(Configuration)
                .EnableNoLogo()
                .EnableNoRestore()
                .SetProjectFile(Solution.GitSnapshotter_Console));
        });

    Target Publish => _ => _
        .Requires(() => PublishDirectory)
        .DependsOn(Compile)
        .Executes(async () =>
        {
            DotNetPublish(_ => _
                .EnableNoLogo()
                .EnableNoBuild()
                .EnableNoRestore()
                .SetConfiguration(Configuration)
                .SetOutput(PublishDirectory)
                .SetProject(Solution.GitSnapshotter_Console)
            );
            
            await OutputToGithub("version", DateTime.Now.ToString("yyyyMMddHHmmss"));
        });

    async Task OutputToGithub(string name, object content)
    {
        await File.AppendAllTextAsync(GithubOutput, $"{name}={content}\n");
    }
}