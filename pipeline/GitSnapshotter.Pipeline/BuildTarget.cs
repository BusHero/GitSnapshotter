namespace GitSnapshotter.Pipeline;

/// <summary>
/// Which modules a pipeline run registers (<c>Build__Target</c>, or <c>--Build:Target=Test</c>). Each GitHub Actions
/// job runs one target, so the workflows only set up the machine and hand files between jobs.
/// </summary>
public enum BuildTarget
{
    /// <summary>
    /// The commit stage: the version, format check, compile, tests, third-party notices, the console app, the packages
    /// and the release candidate (<see cref="BuildSettings.ReleaseDirectory"/>), which a release later publishes as is.
    /// </summary>
    All,

    /// <summary>
    /// Checks that the release candidate <see cref="BuildSettings.Version"/>, built from
    /// <see cref="BuildSettings.Commit"/>, may be released, and writes the release description from CHANGELOG.md.
    /// Builds nothing. Needs a GitHub token (<c>GITHUB_TOKEN</c>) that can read the repository's environments.
    /// </summary>
    VerifyRelease,

    /// <summary>
    /// Runs the smoke tests (pipeline/GitSnapshotter.SmokeTests) on <see cref="BuildSettings.Version"/>: they install
    /// the packages from a feed (a release candidate's directory, or nuget.org) and use them as a user would. Their settings are <c>SmokeTest__*</c> environment
    /// variables. Builds nothing of the product.
    /// </summary>
    SmokeTest,

    /// <summary>Compiles the solution and runs the tests; the format check and the packages are left out.</summary>
    Test,

    /// <summary>Compiles the solution, for CodeQL to watch.</summary>
    Compile,

    /// <summary>Restores the solution, for the dependency graph submission.</summary>
    Restore,

    /// <summary>Builds the documentation site (docs/dist) with npm.</summary>
    Docs,
}
