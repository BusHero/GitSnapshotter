using GitSnapshotter.Pipeline.Modules;

using ModularPipelines;
using ModularPipelines.Extensions;
using ModularPipelines.Modules;

namespace GitSnapshotter.Pipeline;

/// <summary>Where <see cref="BuildTargets.Register"/> adds modules: the pipeline, or a list in the tests.</summary>
internal interface IModuleRegistry
{
    IModuleRegistry Add<TModule>()
        where TModule : class, IModule;
}

/// <summary>The modules each <see cref="BuildTarget"/> runs.</summary>
internal static class BuildTargets
{
    public static void Register(BuildTarget target, IModuleRegistry modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        switch (target)
        {
            case BuildTarget.All:
                modules
                    .Add<VerifyFormattingModule>()
                    .Add<CompileModule>()
                    .Add<ConfigureGitIdentityModule>()
                    .Add<RunUnitTestsModule>()
                    .Add<ThirdPartyNoticesModule>()
                    .Add<PackModule>()
                    .Add<PublishConsoleModule>()
                    .Add<PushPackageModule>()
                    .Add<ReleaseAssetsModule>();
                break;

            case BuildTarget.VerifyRelease:
                modules
                    .Add<ReleaseVersionModule>()
                    .Add<VerifyReleaseModule>()
                    .Add<ReleaseNotesModule>();
                break;

            case BuildTarget.SmokeTest:
                modules.Add<SmokeTestModule>();
                break;

            case BuildTarget.Test:
                modules
                    .Add<CompileModule>()
                    .Add<ConfigureGitIdentityModule>()
                    .Add<RunUnitTestsModule>();
                break;

            case BuildTarget.Compile:
                modules.Add<CompileModule>();
                break;

            case BuildTarget.Restore:
                modules.Add<RestoreModule>();
                break;

            case BuildTarget.Docs:
                modules.Add<BuildDocsModule>();
                break;

            default:
                throw new InvalidOperationException($"Unknown build target '{target}'.");
        }
    }

    /// <summary>Adds the modules of <paramref name="target"/> to a pipeline.</summary>
    public static PipelineBuilder AddModules(this PipelineBuilder builder, BuildTarget target)
    {
        Register(target, new PipelineRegistry(builder));
        return builder;
    }

    private sealed class PipelineRegistry(PipelineBuilder builder) : IModuleRegistry
    {
        public IModuleRegistry Add<TModule>()
            where TModule : class, IModule
        {
            builder.AddModule<TModule>();
            return this;
        }
    }
}
