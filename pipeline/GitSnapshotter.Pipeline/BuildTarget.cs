namespace GitSnapshotter.Pipeline;

/// <summary>
/// Which modules a pipeline run registers (<c>Build__Target</c>, or <c>--Build:Target=Test</c>). Each GitHub Actions
/// job runs one target, so the workflows only set up the machine and hand files between jobs.
/// </summary>
public enum BuildTarget
{
    /// <summary>
    /// The whole build: format check, compile, tests, third-party notices, the console app, the packages, the push
    /// to <see cref="BuildSettings.NuGetSource"/> and, on a release, the release assets.
    /// </summary>
    All,

    /// <summary>
    /// Checks that <see cref="BuildSettings.Version"/> may be released and writes the release description from
    /// CHANGELOG.md. Needs a GitHub token (<c>GITHUB_TOKEN</c>) that can read the repository's environments.
    /// </summary>
    VerifyRelease,

    /// <summary>
    /// Runs the smoke tests (pipeline/GitSnapshotter.SmokeTests) on <see cref="BuildSettings.Version"/>: they download
    /// the published packages and use them as a user would. Their settings are <c>SmokeTest__*</c> environment
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
