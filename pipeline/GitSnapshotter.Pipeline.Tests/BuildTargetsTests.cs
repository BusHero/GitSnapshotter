using System.Reflection;

using GitSnapshotter.Pipeline.Modules;

using ModularPipelines.Modules;

using TUnit.Assertions.Enums;
using TUnit.Core;

using ModuleDependency = ModularPipelines.Attributes.DependsOnAttribute;

namespace GitSnapshotter.Pipeline.Tests;

public sealed class BuildTargetsTests
{
    private sealed class RecordingRegistry : IModuleRegistry
    {
        public List<Type> Modules { get; } = [];

        public IModuleRegistry Add<TModule>()
            where TModule : class, IModule
        {
            Modules.Add(typeof(TModule));
            return this;
        }
    }

    private static List<Type> ModulesOf(BuildTarget target)
    {
        var registry = new RecordingRegistry();
        BuildTargets.Register(target, registry);
        return registry.Modules;
    }

    private static IEnumerable<ModuleDependency> DependenciesOf(Type module) =>
        module.GetCustomAttributes<ModuleDependency>(inherit: true);

    public static IEnumerable<BuildTarget> Targets() => Enum.GetValues<BuildTarget>();

    [Test]
    public async Task AllBuildsTestsAndPacksTheReleaseCandidate()
    {
        await Assert.That(ModulesOf(BuildTarget.All)).IsEquivalentTo(
        [
            typeof(BuildVersionModule),
            typeof(VerifyFormattingModule),
            typeof(CompileModule),
            typeof(ConfigureGitIdentityModule),
            typeof(RunUnitTestsModule),
            typeof(ThirdPartyNoticesModule),
            typeof(PackModule),
            typeof(PublishConsoleModule),
            typeof(ReleaseAssetsModule),
        ]);
    }

    [Test]
    public async Task VerifyReleaseOnlyChecksAndDescribesTheRelease()
    {
        // The verify job runs before anything is built: no module here may compile, pack or push.
        await Assert.That(ModulesOf(BuildTarget.VerifyRelease)).IsEquivalentTo(
            [typeof(ReleaseVersionModule), typeof(VerifyReleaseModule), typeof(ReleaseNotesModule)]);
    }

    [Test]
    public async Task TestCompilesAndRunsTheTestsOnly()
    {
        await Assert.That(ModulesOf(BuildTarget.Test)).IsEquivalentTo(
            [typeof(BuildVersionModule), typeof(CompileModule), typeof(ConfigureGitIdentityModule), typeof(RunUnitTestsModule)]);
    }

    [Test]
    public async Task CompileOnlyCompiles()
    {
        await Assert.That(ModulesOf(BuildTarget.Compile)).IsEquivalentTo([typeof(BuildVersionModule), typeof(CompileModule)]);
    }

    [Test]
    [Arguments(BuildTarget.Restore, typeof(RestoreModule))]
    [Arguments(BuildTarget.Docs, typeof(BuildDocsModule))]
    [Arguments(BuildTarget.SmokeTest, typeof(SmokeTestModule))]
    public async Task SingleModuleTargets(BuildTarget target, Type module)
    {
        await Assert.That(ModulesOf(target)).IsEquivalentTo([module]);
    }

    [Test]
    [MethodDataSource(nameof(Targets))]
    public async Task EveryTargetRegistersTheModulesItsModulesNeed(BuildTarget target)
    {
        // ModularPipelines fails at run time when a module depends on one that isn't registered.
        var modules = ModulesOf(target);

        var missing = modules
            .SelectMany(module => DependenciesOf(module)
                .Where(dependency => !dependency.Optional && !modules.Contains(dependency.Type))
                .Select(dependency => $"{module.Name} needs {dependency.Type.Name}"))
            .ToList();

        await Assert.That(missing).IsEmpty();
    }

    [Test]
    [MethodDataSource(nameof(Targets))]
    public async Task EveryTargetRegistersEachModuleOnce(BuildTarget target)
    {
        var modules = ModulesOf(target);

        await Assert.That(modules).IsNotEmpty();
        await Assert.That(modules.Distinct()).Count().IsEqualTo(modules.Count);
    }

    [Test]
    public async Task EveryModuleIsInATarget()
    {
        var registered = Targets().SelectMany(ModulesOf).ToHashSet();
        var modules = typeof(BuildTargets).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && typeof(IModule).IsAssignableFrom(type)
                && type.Namespace == typeof(CompileModule).Namespace);

        await Assert.That(modules.Where(module => !registered.Contains(module)).Select(module => module.Name)).IsEmpty();
    }

    [Test]
    public async Task SmokeTestBuildsNothing()
    {
        // It tests a release candidate or a release, so it must not compile or pack anything of its own.
        await Assert.That(DependenciesOf(typeof(SmokeTestModule))).IsEmpty();
    }

    [Test]
    [Arguments(typeof(CompileModule))]
    [Arguments(typeof(PackModule))]
    [Arguments(typeof(PublishConsoleModule))]
    [Arguments(typeof(ReleaseAssetsModule))]
    public async Task EverythingBuiltWaitsForTheVersion(Type module)
    {
        // Each stamps or checks the build's version, so none may start before it is known.
        await Assert.That(DependenciesOf(module).Select(d => d.Type)).Contains(typeof(BuildVersionModule));
    }

    [Test]
    public async Task UnknownTargetFails()
    {
        await Assert.That(() => BuildTargets.Register((BuildTarget)99, new RecordingRegistry()))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("'99'", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReleaseAssetsWaitForTheTests()
    {
        // No release candidate is made unless the tests passed.
        await Assert.That(DependenciesOf(typeof(ReleaseAssetsModule)).Select(d => d.Type)).Contains(typeof(RunUnitTestsModule));
    }
}
