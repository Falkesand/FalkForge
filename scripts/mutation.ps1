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
# Do not trust an absolute score from a single run in isolation: docs/testing/
# mutation-testing.md's "Mode discrepancy" section records a same-code case where
# coverage-analysis perTest vs. off disagreed by ~25 percentage points.
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
# Both lookups go through .PSObject.Properties[...] rather than dot-access because
# Set-StrictMode -Version Latest throws "The property '...' cannot be found on this
# object" on a missing key under dot-access (reproduced against
# tests/FalkForge.Integration.Tests/stryker-config.json, which has no
# "coverage-analysis" key) — a config lacking the key should warn or skip, not crash.
$config = Get-Content $configFile -Raw | ConvertFrom-Json
$strykerConfigSection = $config.'stryker-config'

$configuredCoverageAnalysis = $null
if ($strykerConfigSection.PSObject.Properties['coverage-analysis']) {
    $configuredCoverageAnalysis = $strykerConfigSection.'coverage-analysis'
}
if ($configuredCoverageAnalysis -and $configuredCoverageAnalysis -ne $CoverageAnalysis) {
    Write-Host "  WARNING: $configFile sets coverage-analysis='$configuredCoverageAnalysis', which differs from -CoverageAnalysis '$CoverageAnalysis'. The config file wins — dotnet-stryker has no CLI flag for this setting." -ForegroundColor Yellow
} elseif (-not $configuredCoverageAnalysis) {
    Write-Host "  NOTE: $configFile has no 'coverage-analysis' key — Stryker will use its own default, not -CoverageAnalysis '$CoverageAnalysis'." -ForegroundColor Yellow
}

# Sanity check: make sure -SourceProject actually matches the project this config
# mutates, so results never get filed under the wrong project name.
$configuredProject = $null
if ($strykerConfigSection.PSObject.Properties['project']) {
    $configuredProject = $strykerConfigSection.'project'
}
if ($configuredProject -and $configuredProject -ne "$SourceProject.csproj") {
    Write-Host "  FATAL: $configFile sets project='$configuredProject', which does not match -SourceProject '$SourceProject' (expected '$SourceProject.csproj'). Stryker would mutate '$configuredProject' while reports get filed under -SourceProject's output path — refusing to run rather than misfile results under the wrong project name. Fix -SourceProject (or the config's 'project' key) and re-run." -ForegroundColor Red
    exit 1
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

# Captured immediately before invoking Stryker so the report-freshness check below can
# tell "this run just wrote these reports" apart from "these reports are leftovers from
# a previous run into the same reused $Output directory" — see the freshness check after
# the run for why file *existence* alone is not sufficient proof of success.
$runStartTime = Get-Date

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

# With an explicit --output, Stryker writes reports directly under
# <Output>/reports/ (no timestamped subfolder) — but search recursively anyway rather
# than assuming a fixed depth, since that has not been verified across every Stryker
# version/config combination.
$jsonReport = Get-ChildItem -Path $Output -Filter "mutation-report.json" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
$htmlReport = Get-ChildItem -Path $Output -Filter "mutation-report.html" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1

if ($htmlReport) {
    Write-Host "  HTML report : $($htmlReport.FullName)"
}
if ($jsonReport) {
    Write-Host "  JSON report : $($jsonReport.FullName)"
}

# Only "json" and "html" reporters write a report file to disk — "cleartext",
# "progress", "dots", etc. only print to the console and never produce an artifact.
# Require exactly the file(s) the config's own "reporters" array asks for: a config
# that only lists "json" must not fail for a missing HTML file, and vice versa.
#
# Existence alone is not proof this run produced the report: $Output defaults to
# artifacts/mutation/<SourceProject> and is reused across runs, so a prior run's
# mutation-report.json/.html sitting there would satisfy an existence-only check even
# when dotnet-stryker just silently produced nothing (the "No project found" /
# Buildalyzer trap documented above). Require LastWriteTime at or after $runStartTime,
# captured immediately before invoking dotnet-stryker, so a stale leftover reads as a
# failure instead of a false success. A failed run's own reports are never deleted —
# they are left in place as evidence for whoever investigates.
$fileReporterToFileName = @{
    'json' = 'mutation-report.json'
    'html' = 'mutation-report.html'
}
$reportFileByName = @{
    'mutation-report.json' = $jsonReport
    'mutation-report.html' = $htmlReport
}

$configuredReporters = @()
if ($strykerConfigSection.PSObject.Properties['reporters']) {
    $configuredReporters = @($strykerConfigSection.'reporters')
}

$requiredReportFiles = @($configuredReporters | Where-Object { $fileReporterToFileName.ContainsKey($_) } | ForEach-Object { $fileReporterToFileName[$_] })
$missingRequiredReportFiles = @($requiredReportFiles | Where-Object { -not $reportFileByName[$_] })
$staleRequiredReportFiles = @($requiredReportFiles | Where-Object { $reportFileByName[$_] -and $reportFileByName[$_].LastWriteTime -lt $runStartTime })

if ($missingRequiredReportFiles.Count -gt 0) {
    Write-Host "  Missing report file(s) requested by $configFile's 'reporters' list: $($missingRequiredReportFiles -join ', ')" -ForegroundColor Red
}
if ($staleRequiredReportFiles.Count -gt 0) {
    Write-Host "  Stale report file(s) requested by $configFile's 'reporters' list — present but last written before this run started ($runStartTime), so they are leftovers from a previous run into the same reused output folder, not proof of this run: $($staleRequiredReportFiles -join ', ')" -ForegroundColor Red
}

Write-Host ""
Write-Host "The mutation score itself is printed by dotnet-stryker's own reporters above (ClearText/Progress) — this script does not re-derive or re-print it, to avoid parsing a report schema it cannot independently verify right now."

if ($strykerExit -ne 0) {
    Write-Host "Mutation run complete — dotnet-stryker exited $strykerExit." -ForegroundColor Red
    exit $strykerExit
}

# Fail loud rather than silently "succeeding": dotnet-stryker exiting 0 while missing a
# report file its own config asked for, or leaving only a stale one from a previous run
# into this same reused output folder, is exactly the "No project found" silent-failure
# shape documented above (the global.json / Buildalyzer trap) — never let that read as
# a clean run.
if ($missingRequiredReportFiles.Count -gt 0 -or $staleRequiredReportFiles.Count -gt 0) {
    if ($missingRequiredReportFiles.Count -gt 0) {
        Write-Host "Mutation run FAILED — dotnet-stryker exited 0 but is missing report file(s) its own 'reporters' config requested: $($missingRequiredReportFiles -join ', '). This is the silent-success shape of the 'No project found' / Buildalyzer trap documented above; check the console output for 'No project found' or zero mutants created." -ForegroundColor Red
    }
    if ($staleRequiredReportFiles.Count -gt 0) {
        Write-Host "Mutation run FAILED — dotnet-stryker exited 0 but report file(s) its own 'reporters' config requested were not refreshed by this run: $($staleRequiredReportFiles -join ', '). They predate this run (started $runStartTime) and are leftovers from a previous run into this same reused output folder ($Output) — not evidence this run produced anything. Left in place for inspection; check the console output for 'No project found' or zero mutants created." -ForegroundColor Red
    }
    exit 1
}

Write-Host "Mutation run complete." -ForegroundColor Green
