#!/usr/bin/env pwsh
# coverage.ps1 — measure code coverage for FalkForge and generate a local report
#
# Why dotnet-coverage instead of `dotnet test --collect:"XPlat Code Coverage"`:
#   xunit.v3 test projects run on Microsoft.Testing.Platform (MTP), not classic
#   VSTest — the coverlet.collector data collector never attaches under MTP, so
#   `--collect` is a silent no-op. dotnet-coverage wraps the whole test
#   invocation and attaches via CLR profiler instead, which works regardless
#   of test host.
#
# Tools required (globally installed):
#   dotnet-coverage                 — dotnet tool install -g dotnet-coverage
#   dotnet-reportgenerator-globaltool — dotnet tool install -g dotnet-reportgenerator-globaltool
#
# Usage:
#   ./scripts/coverage.ps1                  # CWD-independent — paths resolve via $PSScriptRoot
#   ./scripts/coverage.ps1 -Configuration Debug

param(
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$slnx = Join-Path $root "FalkForge.slnx"
$resultsDir = Join-Path $root "TestResults"
$coverageFile = Join-Path $resultsDir "coverage.cobertura.xml"
$reportDir = Join-Path $root "CoverageReport"

Write-Host ""
Write-Host "FalkForge Coverage Run" -ForegroundColor Cyan
Write-Host "=======================" -ForegroundColor Cyan
Write-Host "Solution      : $slnx"
Write-Host "Configuration : $Configuration"
Write-Host "Date          : $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
Write-Host ""

# ---------------------------------------------------------------------------
# 1. Build
# ---------------------------------------------------------------------------
Write-Host "[1/3] Build ($Configuration)..." -ForegroundColor Yellow

dotnet build $slnx -c $Configuration
$buildExit = $LASTEXITCODE
if ($buildExit -ne 0) {
    Write-Host "  Build failed with exit code $buildExit — aborting." -ForegroundColor Red
    exit $buildExit
}
Write-Host "  Build: OK" -ForegroundColor Green
Write-Host ""

# ---------------------------------------------------------------------------
# 2. Test under dotnet-coverage
# ---------------------------------------------------------------------------
Write-Host "[2/3] Test with coverage..." -ForegroundColor Yellow

# The trx is produced by the Microsoft.Testing.Platform native TRX reporter (arguments after
# the `--` are passed to the test host), NOT the classic VSTest `--logger trx`, which is a
# silent no-op under MTP (xunit.v3 test projects run on MTP, not classic VSTest) and would
# leave this report with no per-test results at all.
dotnet-coverage collect -f cobertura -o "$coverageFile" "dotnet test `"$slnx`" --no-build -c $Configuration -v minimal -- --report-trx --report-trx-filename test-results.trx"
$testExit = $LASTEXITCODE

if ($testExit -ne 0) {
    Write-Host "  Tests failed with exit code $testExit." -ForegroundColor Red
} else {
    Write-Host "  Tests: OK" -ForegroundColor Green
}
Write-Host ""

# ---------------------------------------------------------------------------
# 3. Report
# ---------------------------------------------------------------------------
Write-Host "[3/3] Generate coverage report..." -ForegroundColor Yellow

if (-not (Test-Path $coverageFile)) {
    Write-Host "  No coverage file at $coverageFile — skipping report generation." -ForegroundColor Red
    exit 1
}

# Src-only: keep FalkForge.* production assemblies, drop test assemblies (anything
# ending .Tests or containing .Tests., e.g. FalkForge.Compiler.Msi.Tests.FakeSigil).
# Demo projects build under a separate solution and never enter this cobertura file,
# so the *Demo* exclusion is defensive only. Mirrors .github/workflows/ci.yml.
reportgenerator "-reports:$coverageFile" "-targetdir:$reportDir" "-reporttypes:JsonSummary;TextSummary;Html" "-assemblyfilters:+FalkForge.*;-FalkForge.*.Tests;-FalkForge.*.Tests.*;-*Demo*"
$reportExit = $LASTEXITCODE

$summaryFile = Join-Path $reportDir "Summary.txt"
if (Test-Path $summaryFile) {
    Write-Host ""
    Get-Content $summaryFile | ForEach-Object { Write-Host "  $_" }
}

Write-Host ""

if ($testExit -ne 0) {
    Write-Host "Coverage run complete — tests failed, see above." -ForegroundColor Red
    exit $testExit
}
if ($reportExit -ne 0) {
    Write-Host "Coverage run complete — report generation failed." -ForegroundColor Red
    exit $reportExit
}

Write-Host "Coverage run complete — report at $reportDir\index.html" -ForegroundColor Green
