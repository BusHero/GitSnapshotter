#Requires -Version 7.0

<#
.SYNOPSIS
Runs the smoke tests (pipeline/GitSnapshotter.SmokeTests): downloads the packages and uses them as a user would.

.DESCRIPTION
Sets every SmokeTest__* variable the chosen mode needs, runs the tests, and puts the variables back as they were.

Published (the default): tests a version on nuget.org, with every check, the release-only ones included (signatures,
nuget.org's metadata, the symbol server, the GitHub release, the docs page).

Local (-Local): builds and packs this checkout as CI does, and tests those packages from ./publish, with the
release-only checks off. Our packages come from ./publish, everything else from nuget.org. The Source Link test
downloads the sources from GitHub at the current commit, so it passes only once that commit is pushed.

Needs the .NET SDK from global.json, the .NET 8 runtime, git, and access to nuget.org and GitHub.

.PARAMETER Version
The version to test, like 0.3.0 or 0.4.0-rc.1. Published: defaults to the newest version on nuget.org. Local: taken
from the packages in ./publish.

.PARAMETER WithReleaseAssets
Also downloads the GitHub release's files and checks the packages, PDBs and console app zip against them.

.PARAMETER Local
Tests the packages this checkout builds instead of a published version.

.PARAMETER NoBuild
With -Local: tests the packages already in ./publish instead of building them again.

.PARAMETER WaitMinutes
How long to keep retrying while the feed doesn't list the packages, their metadata or symbols yet. nuget.org takes a
few minutes after a push; a version published a while ago needs no wait, and a check that can't succeed fails
sooner.

.PARAMETER TestArguments
Passed on to the test app, like --treenode-filter "/*/*/ToolTests/*" to run one class, or --list-tests.

.EXAMPLE
./smoke-test.ps1
Tests the newest version on nuget.org.

.EXAMPLE
./smoke-test.ps1 -Version 0.4.0-rc.1 -WaitMinutes 30
Tests a version just released, waiting up to 30 minutes for nuget.org to list it, its metadata and its symbols.

.EXAMPLE
./smoke-test.ps1 -Version 0.3.0 -WithReleaseAssets
Tests 0.3.0, and compares it with the files of the v0.3.0 GitHub release.

.EXAMPLE
./smoke-test.ps1 -Local
Builds and packs this checkout, then tests those packages.

.EXAMPLE
./smoke-test.ps1 -Local -NoBuild --treenode-filter "/*/*/ToolTests/*"
Tests the packages already in ./publish, running only the tool's tests.
#>
[CmdletBinding(DefaultParameterSetName = 'Published')]
param(
    [Parameter(ParameterSetName = 'Published', Position = 0)]
    [string] $Version,

    [Parameter(ParameterSetName = 'Published')]
    [switch] $WithReleaseAssets,

    [Parameter(ParameterSetName = 'Local', Mandatory)]
    [switch] $Local,

    [Parameter(ParameterSetName = 'Local')]
    [switch] $NoBuild,

    [int] $WaitMinutes = 2,

    [Parameter(ValueFromRemainingArguments)]
    [string[]] $TestArguments = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = $PSScriptRoot
$publish = Join-Path $root 'publish'
$smokeTests = Join-Path $root 'pipeline' 'GitSnapshotter.SmokeTests'

function Invoke-Native {
    # Runs a command and stops the script if it fails.
    param([string] $File, [string[]] $Arguments)
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$File $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

# What the tests need, checked first so they don't fail half way through.
foreach ($command in 'dotnet', 'git') {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "$command is not installed; the smoke tests need it."
    }
}
if (-not (& dotnet --list-runtimes | Select-String -SimpleMatch 'Microsoft.NETCore.App 8.')) {
    Write-Warning 'The .NET 8 runtime is not installed: the tests that run the library and the tool on .NET 8 will fail.'
}

$settings = [ordered]@{}

if ($Local) {
    if (-not $NoBuild) {
        # As CI builds: CI=true maps the sources to /_/ in the PDBs (ContinuousIntegrationBuild), which the
        # deterministic build test checks, and restores from the lock files.
        Write-Host 'Building and packing this checkout as CI does (the build pipeline)...' -ForegroundColor Cyan
        $ci = $env:CI
        $env:CI = 'true'
        try {
            Invoke-Native 'dotnet' @('run', '--project', (Join-Path $root 'pipeline' 'GitSnapshotter.Pipeline'), '--configuration', 'Release')
        }
        finally {
            if ($null -eq $ci) { Remove-Item Env:CI -ErrorAction SilentlyContinue } else { $env:CI = $ci }
        }
    }

    # The library's package, not the tool's: GitSnapshotter.<version>.nupkg.
    $package = Get-ChildItem -Path $publish -Filter 'GitSnapshotter.*.nupkg' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike 'GitSnapshotter.Tool.*' } |
        Sort-Object LastWriteTime |
        Select-Object -Last 1
    if (-not $package) {
        throw "No GitSnapshotter package in $publish. Run without -NoBuild to build one."
    }
    $Version = $package.Name -replace '^GitSnapshotter\.(.+)\.nupkg$', '$1'

    $commit = (& git -C $root rev-parse HEAD).Trim()
    if (-not (& git -C $root branch --remotes --contains $commit)) {
        Write-Warning "$commit is not pushed: the Source Link test downloads the sources from GitHub at that commit, so it will fail."
    }

    $settings.Source = $publish
    $settings.Commit = $commit
    # As on a CI build: only a release is signed, indexed by nuget.org, on GitHub and in the docs.
    $settings.VerifySignatures = 'false'
    $settings.FeedMetadata = 'false'
    $settings.Symbols = 'false'
    $settings.GitHubRelease = 'false'
    $settings.Docs = 'false'
    $settings.AboveReleases = 'true'
}
else {
    if (-not $Version) {
        $versions = (Invoke-RestMethod 'https://api.nuget.org/v3-flatcontainer/gitsnapshotter.tool/index.json').versions
        $Version = $versions[-1]
    }
    $Version = $Version.TrimStart('v')
    $settings.Source = 'https://api.nuget.org/v3/index.json'

    if ($WithReleaseAssets) {
        # The repository is public: the release and its files need no sign-in.
        $release = Invoke-RestMethod "https://api.github.com/repos/BusHero/GitSnapshotter/releases/tags/v$Version"
        $assets = Join-Path ([System.IO.Path]::GetTempPath()) "gitsnapshotter-release-$Version"
        Remove-Item -Recurse -Force $assets -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Path $assets | Out-Null
        Write-Host "Downloading the v$Version GitHub release's files to $assets..." -ForegroundColor Cyan
        foreach ($asset in $release.assets) {
            Invoke-WebRequest $asset.browser_download_url -OutFile (Join-Path $assets $asset.name)
        }
        $settings.ReleaseDirectory = $assets
    }
}

$settings.Version = $Version
$settings.Root = $root
$settings.WaitMinutes = $WaitMinutes

# Set the variables for the run only; whatever was set before comes back afterwards.
$previous = @{}
foreach ($name in $settings.Keys) {
    $variable = "SmokeTest__$name"
    $previous[$variable] = [Environment]::GetEnvironmentVariable($variable)
    [Environment]::SetEnvironmentVariable($variable, [string] $settings[$name])
}

try {
    Write-Host "Smoke testing $Version from $($settings.Source)" -ForegroundColor Cyan
    foreach ($name in $settings.Keys) {
        Write-Host "  SmokeTest__$name = $($settings[$name])"
    }

    & dotnet run --project $smokeTests --configuration Release -- --output Detailed @TestArguments
    $exitCode = $LASTEXITCODE
}
finally {
    foreach ($variable in $previous.Keys) {
        # Not SetEnvironmentVariable($variable, $null): PowerShell passes $null as "", which sets the variable empty.
        if ($null -eq $previous[$variable]) {
            Remove-Item "Env:$variable" -ErrorAction SilentlyContinue
        }
        else {
            Set-Item "Env:$variable" $previous[$variable]
        }
    }
}

exit $exitCode
