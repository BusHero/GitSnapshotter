using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.ConstrainedExecution;
using System.Threading.Tasks;

using JetBrains.Annotations;

using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitHub;

using static Constants.TestConstants;

using static Nuke.Common.Tools.DotNet.DotNetTasks;

[GitHubActions(
    "build",
    GitHubActionsImage.UbuntuLatest,
    On = [GitHubActionsTrigger.Push],
    AutoGenerate = false,
    InvokedTargets = [nameof(Compile)])]
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
sealed class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Pack);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    private readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution(relativePath: "Source/GitSnapshotter.sln", GenerateProjects = true)]
    private readonly Solution Solution = null!;

    [Parameter("Github Output")] private readonly string GithubOutput = null!;
    [Secret, Parameter("Github key to publish")] public readonly string GithubApiKey = null!;

    [Parameter("Directory to publish stuff to")] public readonly AbsolutePath PublishDirectory = null!;

    [GitRepository] public readonly GitRepository Repository = null!;

    [UsedImplicitly]
    public Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            DotNetClean(_ => _
                .SetConfiguration(Configuration)
                .SetProject(Solution));
        });

    public Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(_ => _
                .SetProjectFile(Solution));
        });

    public Target Compile => _ => _
        .DependsOn(Restore)
        .Triggers(RunUnitTests)
        .Executes(() =>
        {
            DotNetBuild(_ => _
                .SetConfiguration(Configuration)
                .EnableNoLogo()
                .EnableNoRestore()
                .SetProjectFile(Solution));
        });

    [UsedImplicitly]
    public Target Publish => _ => _
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

            var version =
                $"{DateTime.Now:yyyyMMddHHmmss}+{(Repository.Branch == await Repository.GetDefaultBranch() ? "trunk" : "merge")}";

            await OutputToGithub("version", version);
        });

    [UsedImplicitly]
    public Target RunUnitTests => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetTest(_ => _
                .SetProjectFile(Solution)
                .EnableNoBuild()
                .EnableNoRestore()
                .SetFilter($"{Traits.Names.CATEGORY}~{Traits.Values.DISCOVERY}")
                .SetConfiguration(Configuration)
                .EnableNoLogo());
        });

    [UsedImplicitly]
    public Target Pack => _ => _
        .DependsOn(Compile)
        .Requires(() => PublishDirectory)
        .Executes(() =>
        {
            var branch = Repository.Branch?.Replace('/', '-');
            DotNetPack(_ => _
                .SetProject(Solution.GitSnapshotter)
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .SetVersionSuffix($"{branch}-{DateTime.Now:yyyyMMddhhmmss}")
                .SetOutputDirectory(PublishDirectory)
                .EnableNoRestore());
        });

    [UsedImplicitly]
    public Target PublishPackage => _ => _
        .DependsOn(Pack, RunUnitTests)
        .Requires(() => PublishDirectory)
        .Requires(() => GithubApiKey)
        .Executes(() =>
        {
            DotNetNuGetPush(_ => _
                .SetSource("github")
                .EnableSkipDuplicate()
                .SetTargetPath(PublishDirectory / "*.nupkg")
                .SetApiKey(GithubApiKey)
            );
        });

    private async Task OutputToGithub(string name, object content)
    {
        await File.AppendAllTextAsync(
            GithubOutput,
            $"{name}={content}\n");
    }
}