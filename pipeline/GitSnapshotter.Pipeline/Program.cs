using GitSnapshotter.Pipeline;
using GitSnapshotter.Pipeline.Modules;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ModularPipelines;
using ModularPipelines.Extensions;

var builder = Pipeline.CreateBuilder(args);

var section = builder.Configuration.GetSection(BuildSettings.Section);
builder.Services.Configure<BuildSettings>(section);

var target = (section.Get<BuildSettings>() ?? new BuildSettings()).Target;
switch (target)
{
    case BuildTarget.All:
        builder
            .AddModule<VerifyFormattingModule>()
            .AddModule<CompileModule>()
            .AddModule<ConfigureGitIdentityModule>()
            .AddModule<RunUnitTestsModule>()
            .AddModule<ThirdPartyNoticesModule>()
            .AddModule<PackModule>()
            .AddModule<PublishConsoleModule>()
            .AddModule<PushPackageModule>()
            .AddModule<ReleaseAssetsModule>();
        break;

    case BuildTarget.VerifyRelease:
        builder
            .AddModule<ReleaseVersionModule>()
            .AddModule<VerifyReleaseModule>()
            .AddModule<ReleaseNotesModule>();
        break;

    case BuildTarget.Test:
        builder
            .AddModule<CompileModule>()
            .AddModule<ConfigureGitIdentityModule>()
            .AddModule<RunUnitTestsModule>();
        break;

    case BuildTarget.Compile:
        builder.AddModule<CompileModule>();
        break;

    case BuildTarget.Restore:
        builder.AddModule<RestoreModule>();
        break;

    case BuildTarget.Docs:
        builder.AddModule<BuildDocsModule>();
        break;

    default:
        throw new InvalidOperationException($"Unknown build target '{target}'.");
}

var pipeline = await builder.BuildAsync();
await pipeline.RunAsync();
