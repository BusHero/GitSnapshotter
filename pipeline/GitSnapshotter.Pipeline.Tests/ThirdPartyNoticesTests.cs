using TUnit.Assertions.Enums;
using TUnit.Core;

namespace GitSnapshotter.Pipeline.Tests;

public sealed class ThirdPartyNoticesTests : IDisposable
{
    private const string Mit = """<license type="expression">MIT</license>""";

    private readonly FakeRestore _restore = new();

    public void Dispose() => _restore.Dispose();

    [Test]
    public async Task PackagesThatShipFilesAreListedInOrder()
    {
        _restore.AddPackage("Zeta", "1.0.0", Mit, ("runtime", "lib/net8.0/Zeta.dll"));
        _restore.AddPackage("alpha", "2.0.0", Mit, ("native", "runtimes/linux-x64/native/libalpha.so"));
        _restore.AddPackage("Beta", "3.0.0", Mit, ("compile", "ref/net8.0/Beta.dll"), ("runtime", "lib/net8.0/_._"));

        var packages = await _restore.WriteNoticesAsync(ThirdPartyNotices.AppAssets);

        // Beta only takes part in compilation (its runtime file is a placeholder), so it isn't shipped.
        await Assert.That(packages).IsEquivalentTo(["alpha 2.0.0", "Zeta 1.0.0"], CollectionOrdering.Matching);
        var notices = await _restore.ReadNoticesAsync();
        await Assert.That(notices).Contains("  - alpha 2.0.0\n  - Zeta 1.0.0\n");
        await Assert.That(notices).DoesNotContain("Beta");
    }

    [Test]
    public async Task ProjectReferencesAreNotListed()
    {
        _restore.AddPackage("Package", "1.0.0", Mit, ("runtime", "lib/net8.0/Package.dll"));
        _restore.AddToTarget("net11.0", "Constants/1.0.0", "project", ("runtime", "bin/placeholder/Constants.dll"));

        var packages = await _restore.WriteNoticesAsync(ThirdPartyNotices.AppAssets);

        await Assert.That(packages).IsEquivalentTo(["Package 1.0.0"]);
    }

    [Test]
    public async Task APackageShippedByAnyFrameworkIsListedOnce()
    {
        _restore.AddPackage("Polyfill", "1.0.0", Mit, ("contentFiles", "contentFiles/cs/net472/Polyfill.cs"));
        _restore.AddToTarget("net472", "Polyfill/1.0.0", "package", ("contentFiles", "contentFiles/cs/net472/Polyfill.cs"));
        _restore.AddToTarget("net8.0", "Polyfill/1.0.0", "package", ("contentFiles", "contentFiles/cs/_._"));

        var packages = await _restore.WriteNoticesAsync(ThirdPartyNotices.PackageAssets);

        await Assert.That(packages).IsEquivalentTo(["Polyfill 1.0.0"]);
    }

    [Test]
    public async Task PackageNoticesCountOnlyCompiledInSources()
    {
        // A library package's dependencies are restored by its consumers, so only compiled-in sources are redistributed.
        _restore.AddPackage("Dependency", "1.0.0", Mit, ("runtime", "lib/net8.0/Dependency.dll"));
        _restore.AddPackage("Sources", "1.0.0", Mit, ("contentFiles", "contentFiles/cs/any/Code.cs"));

        var packages = await _restore.WriteNoticesAsync(ThirdPartyNotices.PackageAssets);

        await Assert.That(packages).IsEquivalentTo(["Sources 1.0.0"]);
    }

    [Test]
    public async Task EntryHasTheProjectCopyrightAndLicenseFromTheNuspec()
    {
        _restore.AddPackage(
            "Package",
            "1.2.3",
            $"""
            {Mit}
            <projectUrl>https://example.com/package</projectUrl>
            <copyright>  Copyright (c) Package Authors  </copyright>
            """,
            ("runtime", "lib/net8.0/Package.dll"));

        await _restore.WriteNoticesAsync(ThirdPartyNotices.AppAssets);

        var notices = await _restore.ReadNoticesAsync();
        await Assert.That(notices).Contains(
            """
            ================================================================================
            Package 1.2.3
            ================================================================================
            Project:   https://example.com/package
            Copyright: Copyright (c) Package Authors
            License:   MIT

            Permission is hereby granted, free of charge,
            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task HeaderNamesTheProduct()
    {
        _restore.AddPackage("Package", "1.0.0", Mit, ("runtime", "lib/net8.0/Package.dll"));

        await _restore.WriteNoticesAsync(ThirdPartyNotices.AppAssets);

        await Assert.That(await _restore.ReadNoticesAsync())
            .StartsWith("Product uses the third-party software listed below.");
    }

    [Test]
    public async Task MissingCopyrightFallsBackToTheKnownHolderOrSaysSo()
    {
        _restore.AddPackage("Polyfill", "1.0.0", Mit, ("contentFiles", "contentFiles/cs/any/Polyfill.cs"));
        _restore.AddPackage("Unknown", "1.0.0", $"{Mit}<copyright> </copyright>", ("runtime", "lib/net8.0/Unknown.dll"));

        await _restore.WriteNoticesAsync(ThirdPartyNotices.AppAssets);

        var notices = await _restore.ReadNoticesAsync();
        await Assert.That(notices).Contains("Polyfill 1.0.0\n" + new string('=', 80) + "\nCopyright: Copyright (c) Simon Cropp\n");
        await Assert.That(notices).Contains("Unknown 1.0.0\n" + new string('=', 80) + "\nCopyright: not stated by the package;");
    }

    [Test]
    public async Task LicenseFileFromThePackageIsIncluded()
    {
        var directory = _restore.AddPackage(
            "Package",
            "1.0.0",
            """<license type="file">docs/LICENSE.txt</license>""",
            ("runtime", "lib/net8.0/Package.dll"));
        Directory.CreateDirectory(Path.Combine(directory, "docs"));
        await File.WriteAllTextAsync(Path.Combine(directory, "docs", "LICENSE.txt"), "﻿\r\nThe license text.\r\nLine two.\r\n\r\n");

        await _restore.WriteNoticesAsync(ThirdPartyNotices.AppAssets);

        await Assert.That(await _restore.ReadNoticesAsync())
            .Contains("License:   see below (from docs/LICENSE.txt in the package)\n\nThe license text.\nLine two.\n");
    }

    [Test]
    [Arguments("../OTHER/1.0.0/LICENSE.txt")]
    [Arguments("../../secret.txt")]
    public async Task LicenseFileOutsideThePackageIsRefused(string licenseFile)
    {
        _restore.AddPackage("Package", "1.0.0", $"""<license type="file">{licenseFile}</license>""", ("runtime", "lib/net8.0/Package.dll"));
        _restore.AddPackage("Other", "1.0.0", Mit);
        await File.WriteAllTextAsync(Path.Combine(_restore.PackagesFolder, "OTHER", "1.0.0", "LICENSE.txt"), "Not this one.");
        await File.WriteAllTextAsync(Path.Combine(_restore.PackagesFolder, "..", "secret.txt"), "Secret.");

        await Assert.That(async () => { await _restore.WriteNoticesAsync(ThirdPartyNotices.AppAssets); })
            .Throws<InvalidOperationException>()
            .WithMessageContaining("outside the package", StringComparison.Ordinal);
        await Assert.That(File.Exists(_restore.OutputFile)).IsFalse();
    }

    [Test]
    [Arguments("""<license type="expression">Apache-2.0</license>""", "'Apache-2.0' (expression)")]
    [Arguments("""<license type="expression">MIT OR Apache-2.0</license>""", "'MIT OR Apache-2.0' (expression)")]
    [Arguments("""<licenseUrl>https://example.com/license</licenseUrl>""", "(none)")]
    [Arguments("", "(none)")]
    public async Task LicenseWithoutAKnownTextFailsTheBuild(string metadata, string described)
    {
        _restore.AddPackage("Package", "1.0.0", metadata, ("runtime", "lib/net8.0/Package.dll"));

        await Assert.That(async () => { await _restore.WriteNoticesAsync(ThirdPartyNotices.AppAssets); })
            .Throws<InvalidOperationException>()
            .WithMessageContaining($"Package Package 1.0.0 has license {described}", StringComparison.Ordinal);
    }

    [Test]
    public async Task ThirdPartyNoticesInsideAPackageAreIncluded()
    {
        var directory = _restore.AddPackage("Package", "1.0.0", Mit, ("runtime", "lib/net8.0/Package.dll"));
        await File.WriteAllTextAsync(Path.Combine(directory, "THIRD-PARTY-NOTICES.TXT"), "Bundled code notice.\n");
        await File.WriteAllTextAsync(Path.Combine(directory, "ThirdPartyNotices.txt"), "Second notice.\n");
        await File.WriteAllTextAsync(Path.Combine(directory, "NOTICE.txt"), "Not a third-party notice.\n");

        await _restore.WriteNoticesAsync(ThirdPartyNotices.AppAssets);

        var notices = await _restore.ReadNoticesAsync();
        var light = new string('-', 80);
        await Assert.That(notices).Contains($"{light}\nTHIRD-PARTY-NOTICES.TXT from Package\n{light}\n\nBundled code notice.\n");
        await Assert.That(notices).Contains($"{light}\nThirdPartyNotices.txt from Package\n{light}\n\nSecond notice.\n");
        await Assert.That(notices).DoesNotContain("Not a third-party notice.");
    }

    [Test]
    public async Task OutputUsesLfLineEndings()
    {
        _restore.AddPackage("Package", "1.0.0", Mit, ("runtime", "lib/net8.0/Package.dll"));

        await _restore.WriteNoticesAsync(ThirdPartyNotices.AppAssets);

        await Assert.That(await _restore.ReadNoticesAsync()).DoesNotContain("\r");
    }

    [Test]
    public async Task MissingRestoreGraphAsksForARestore()
    {
        await Assert.That(async () => await ThirdPartyNotices.WriteAsync(
                "Product", _restore.AssetsFile, ThirdPartyNotices.AppAssets, _restore.OutputFile, CancellationToken.None))
            .Throws<FileNotFoundException>()
            .WithMessageContaining("Restore the project first", StringComparison.Ordinal);
    }

    [Test]
    public async Task PackageMissingFromThePackagesFolderAsksForARestore()
    {
        var directory = _restore.AddPackage("Package", "1.0.0", Mit, ("runtime", "lib/net8.0/Package.dll"));
        Directory.Delete(directory, recursive: true);

        await Assert.That(async () => { await _restore.WriteNoticesAsync(ThirdPartyNotices.AppAssets); })
            .Throws<DirectoryNotFoundException>()
            .WithMessageContaining("Package Package/1.0.0 is not in any packages folder", StringComparison.Ordinal);
    }
}
