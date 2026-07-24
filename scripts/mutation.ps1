#!/usr/bin/env pwsh
# mutation.ps1 — run Stryker.NET mutation testing against one FalkForge project
#
# Why --msbuild-path is mandatory here (do not remove this without re-reading the note
# below): this repo's global.json pins sdk.version 10.0.103 with rollForward:
# latestFeature. The plain `dotnet` CLI resolves that fine via rollForward, but Stryker
# hosts MSBuild in-process through Buildalyzer, whose SDK resolution silently fails
# against a pinned-but-not-installed SDK version — no MSBuild error at any verbosity,
# just "No project found" / zero mutants created. Passing --msbuild-path with the SDK
# that `dotnet --version` actually resolves to (from the repo root, honoring
# rollForward) works around it. Confirmed by toggling global.json on/off against an
# out-of-repo copy of a test project: a vanilla net10.0 project with no global.json
# mutates fine, this repo's copy does not until --msbuild-path is passed explicitly.
#
# Why "test-runner": "mtp" in every stryker-config.json: this repo's test projects run
# on xunit.v3 3.2.2 under Microsoft.Testing.Platform (MTP), not classic VSTest. Stryker's
# default vstest runner is unreliable against xunit.v3 (upstream stryker-net#3117). The
# mtp runner (Stryker 4.13+, `-t|--test-runner <vstest,mtp>` in `dotnet-stryker --help`)
# is the supported path here — it is still flagged PREVIEW by upstream Stryker itself,
# expect that banner in the console output.
#
# Why -CoverageAnalysis is a script param but never passed to the CLI: `--coverage-
# analysis` is NOT a recognized dotnet-stryker CLI option (verified: passing it errors
# "Unrecognized option '--coverage-analysis'" — it does not appear in `dotnet-stryker
# --help` at all). It is config-file only. Each wired tests/*/stryker-config.json
# already sets "coverage-analysis". This script instead reads that file back and warns
# if it disagrees with -CoverageAnalysis, so a caller who thinks they overrode it here
# finds out immediately rather than silently mutating under the wrong setting.
#
# Tool required (global):
#   dotnet-stryker — dotnet tool install -g dotnet-stryker
#
# Usage:
#   ./scripts/mutation.ps1 -TestProject FalkForge.Signing.SignServer.Tests -SourceProject FalkForge.Signing.SignServer
#   ./scripts/mutation.ps1 -TestProject FalkForge.Core.Tests -SourceProject FalkForge.Core -Concurrency 8

param(
    [Parameter(Mandatory = $true)]
    [string]$TestProject,

    [Parameter(Mandatory = $true)]
    [string]$SourceProject,

    [string]$CoverageAnalysis = "perTest",

    [int]$Concurrency = 12,

    [string]$Output
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$testProjectDir = Join-Path $root "tests" $TestProject
$configFile = Join-Path $testProjectDir "stryker-config.json"

if (-not (Test-Path $testProjectDir)) {
    Write-Host "Test project folder not found: $testProjectDir" -ForegroundColor Red
    exit 1
}
if (-not (Test-Path $configFile)) {
    Write-Host "No stryker-config.json at $configFile — create one before running mutation testing." -ForegroundColor Red
    exit 1
}

if (-not $Output) {
    $Output = Join-Path $root "artifacts" "mutation" $SourceProject
}

Write-Host ""
Write-Host "FalkForge Mutation Run" -ForegroundColor Cyan
Write-Host "=======================" -ForegroundColor Cyan
Write-Host "Test project      : $TestProject"
Write-Host "Source project    : $SourceProject"
Write-Host "Coverage analysis : $CoverageAnalysis (expected — see config-file check below)"
Write-Host "Concurrency       : $Concurrency"
Write-Host "Output            : $Output"
Write-Host "Date              : $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
Write-Host ""

# Sanity check: -CoverageAnalysis cannot be forwarded to the CLI (see header comment),
# so warn loudly if the config file this run will actually use disagrees with it.
$config = Get-Content $configFile -Raw | ConvertFrom-Json
$configuredCoverageAnalysis = $config.'stryker-config'.'coverage-analysis'
if ($configuredCoverageAnalysis -and $configuredCoverageAnalysis -ne $CoverageAnalysis) {
    Write-Host "  WARNING: $configFile sets coverage-analysis='$configuredCoverageAnalysis', which differs from -CoverageAnalysis '$CoverageAnalysis'. The config file wins — dotnet-stryker has no CLI flag for this setting." -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------
# 1. Resolve the SDK Stryker's in-process MSBuild must use
# ---------------------------------------------------------------------------
Write-Host "[1/2] Resolve SDK for --msbuild-path..." -ForegroundColor Yellow

Push-Location $root
try {
    $sdkVersion = (dotnet --version).Trim()
} finally {
    Pop-Location
}

if ($LASTEXITCODE -ne 0 -or -not $sdkVersion) {
    Write-Host "  Could not resolve SDK version via 'dotnet --version' from $root." -ForegroundColor Red
    exit 1
}

# Resolve the dotnet install root portably instead of hardcoding the default Windows
# path: prefer $env:DOTNET_ROOT when set, else derive it from the directory containing
# whichever `dotnet` executable actually resolves on PATH, falling back to the default
# "C:\Program Files\dotnet" only as a last resort (e.g. DOTNET_ROOT unset and Get-Command
# somehow fails).
if ($env:DOTNET_ROOT) {
    $dotnetRoot = $env:DOTNET_ROOT
} else {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnetCommand) {
        $dotnetRoot = Split-Path $dotnetCommand.Source -Parent
    } else {
        $dotnetRoot = "C:\Program Files\dotnet"
    }
}

$msbuildPath = Join-Path $dotnetRoot "sdk" $sdkVersion "MSBuild.dll"

if (-not (Test-Path $msbuildPath)) {
    Write-Host "  Resolved SDK $sdkVersion but $msbuildPath does not exist." -ForegroundColor Red
    Write-Host "  This means the SDK global.json/rollForward resolves to is not installed at that exact path — install it, or check 'dotnet --list-sdks'." -ForegroundColor Red
    exit 1
}

Write-Host "  SDK          : $sdkVersion"
Write-Host "  MSBuild path : $msbuildPath"
Write-Host ""

# ---------------------------------------------------------------------------
# 2. Run Stryker
# ---------------------------------------------------------------------------
Write-Host "[2/2] Run dotnet-stryker..." -ForegroundColor Yellow

Push-Location $testProjectDir
try {
    dotnet-stryker `
        --config-file "$configFile" `
        --msbuild-path "$msbuildPath" `
        --output "$Output" `
        --concurrency $Concurrency
    $strykerExit = $LASTEXITCODE
} finally {
    Pop-Location
}

Write-Host ""

# Stryker nests its reports under a timestamped subfolder of -O/--output, so search
# recursively rather than assuming a fixed depth.
$jsonReport = Get-ChildItem -Path $Output -Filter "mutation-report.json" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
$htmlReport = Get-ChildItem -Path $Output -Filter "mutation-report.html" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1

if ($htmlReport) {
    Write-Host "  HTML report : $($htmlReport.FullName)"
}
if ($jsonReport) {
    Write-Host "  JSON report : $($jsonReport.FullName)"
}
if (-not $htmlReport -and -not $jsonReport) {
    Write-Host "  No mutation-report.{json,html} found under $Output — check the console output above for the mutation score." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "The mutation score itself is printed by dotnet-stryker's own reporters above (ClearText/Progress) — this script does not re-derive or re-print it, to avoid parsing a report schema it cannot independently verify right now."

if ($strykerExit -ne 0) {
    Write-Host "Mutation run complete — dotnet-stryker exited $strykerExit." -ForegroundColor Red
    exit $strykerExit
}

Write-Host "Mutation run complete." -ForegroundColor Green
