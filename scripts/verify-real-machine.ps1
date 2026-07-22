#!/usr/bin/env pwsh
# verify-real-machine.ps1 — run FalkForge's real-system e2e tests on a real elevated machine
#
# WARNING — THIS SCRIPT MUTATES MACHINE STATE. It creates and removes firewall rules, IIS app
# pools/sites/virtual directories, SQL databases, local users/groups, and SMB file shares. Run it
# ONLY on a disposable Windows VM you're willing to have altered — never on a machine you care
# about. It must run from an elevated (Administrator) shell or every test self-skips.
#
# What this automates:
#   1. Builds the solution (Release by default).
#   2. Sets FALKFORGE_E2E=1 and FALKFORGE_REAL_SYSTEM_E2E=1 for this process only.
#   3. Runs the one test project that holds every real-system e2e test
#      (tests/FalkForge.Compiler.Msi.Tests) — see docs/testing/real-machine-verification.md
#      for the full inventory of which test covers which live path.
#   4. Prints a pass/fail/skip summary read from the produced TRX.
#
# What this does NOT automate — see docs/testing/real-machine-verification.md Part 2 for the
# manual checklist: bundle chain install + rollback boundaries, IIS certificate binding, the
# per-package feature picker's ADDLOCAL filtering, external/downloadable bundle containers, the
# turnkey built-in UI host actually showing a window, per-culture install UI, and the
# Authenticode detach/sign/reattach ceremony. None of those have automated real-machine coverage
# in this repo today; this script cannot verify them for you.
#
# Usage:
#   ./scripts/verify-real-machine.ps1                  # scoped run (fast) — Compiler.Msi.Tests only
#   ./scripts/verify-real-machine.ps1 -Full             # full solution, mirrors CI's E2E run
#   ./scripts/verify-real-machine.ps1 -Configuration Debug
#   ./scripts/verify-real-machine.ps1 -SkipBuild        # reuse a previous build

param(
    [string]$Configuration = "Release",
    [switch]$Full,
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$slnx = Join-Path $root "FalkForge.slnx"
$msiTestsProject = Join-Path $root "tests\FalkForge.Compiler.Msi.Tests\FalkForge.Compiler.Msi.Tests.csproj"
$resultsDir = Join-Path $root "TestResults"
$trxName = if ($Full) { "real-machine-full-e2e.trx" } else { "real-machine-msi-e2e.trx" }

# ---------------------------------------------------------------------------
# 0. Loud warning + elevation check
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "==============================================================" -ForegroundColor Red
Write-Host " FalkForge real-machine verification — MUTATES MACHINE STATE" -ForegroundColor Red
Write-Host " Firewall rules, IIS sites/pools, SQL databases, local users," -ForegroundColor Red
Write-Host " and SMB shares will be created and removed on THIS machine." -ForegroundColor Red
Write-Host " Run this only on a disposable VM you own." -ForegroundColor Red
Write-Host "==============================================================" -ForegroundColor Red
Write-Host ""

$isElevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isElevated) {
    Write-Host "Not running elevated — every real-system test will self-skip." -ForegroundColor Red
    Write-Host "Re-run this script from an Administrator PowerShell." -ForegroundColor Red
    exit 1
}
Write-Host "Elevation check: OK (running as Administrator)" -ForegroundColor Green
Write-Host ""

# ---------------------------------------------------------------------------
# 1. Build
# ---------------------------------------------------------------------------
if ($SkipBuild) {
    Write-Host "[1/3] Skipping build (-SkipBuild)." -ForegroundColor Yellow
} else {
    Write-Host "[1/3] Build ($Configuration)..." -ForegroundColor Yellow
    dotnet build $slnx -c $Configuration
    $buildExit = $LASTEXITCODE
    if ($buildExit -ne 0) {
        Write-Host "  Build failed with exit code $buildExit — aborting." -ForegroundColor Red
        exit $buildExit
    }
    Write-Host "  Build: OK" -ForegroundColor Green
}
Write-Host ""

# ---------------------------------------------------------------------------
# 2. Run the real-system e2e tests
# ---------------------------------------------------------------------------
$env:FALKFORGE_E2E = '1'
$env:FALKFORGE_REAL_SYSTEM_E2E = '1'

if ($Full) {
    Write-Host "[2/3] Running FULL solution e2e (FALKFORGE_E2E=1, FALKFORGE_REAL_SYSTEM_E2E=1)..." -ForegroundColor Yellow
    Write-Host "  This mirrors CI's heavyweight e2e run (full demo-catalog builds, forge verify" -ForegroundColor Yellow
    Write-Host "  --rebuild) PLUS the real-system tests. Expect this to take several minutes." -ForegroundColor Yellow
    $target = $slnx
} else {
    Write-Host "[2/3] Running scoped real-system e2e (FalkForge.Compiler.Msi.Tests only)..." -ForegroundColor Yellow
    $target = $msiTestsProject
}

dotnet test $target -c $Configuration -v minimal -- --report-trx --report-trx-filename $trxName
$testExit = $LASTEXITCODE
Write-Host ""

# ---------------------------------------------------------------------------
# 3. Summarize
# ---------------------------------------------------------------------------
Write-Host "[3/3] Summary" -ForegroundColor Yellow

$trxPath = Get-ChildItem -Path $resultsDir -Filter $trxName -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $trxPath) {
    Write-Host "  No TRX found under $resultsDir matching $trxName — cannot summarize; check dotnet test output above." -ForegroundColor Red
} else {
    [xml]$trx = Get-Content $trxPath.FullName
    $counters = $trx.TestRun.ResultSummary.Counters
    Write-Host "  Total: $($counters.total)  Passed: $($counters.passed)  Failed: $($counters.failed)  Skipped: $($counters.notExecuted)" -ForegroundColor Cyan

    $realSystemTests = $trx.TestRun.Results.UnitTestResult | Where-Object { $_.testName -match 'OnRealInstall|OnRealUninstall|RealInstall' }
    if ($realSystemTests) {
        Write-Host ""
        Write-Host "  Real-system test outcomes:" -ForegroundColor Cyan
        foreach ($t in $realSystemTests) {
            $color = switch ($t.outcome) { "Passed" { "Green" }; "Failed" { "Red" }; default { "DarkYellow" } }
            Write-Host "    [$($t.outcome)] $($t.testName)" -ForegroundColor $color
        }
        Write-Host ""
        Write-Host "  A 'Skipped'/'NotExecuted' outcome above means a prerequisite gate didn't" -ForegroundColor DarkYellow
        Write-Host "  fully open (elevation, FALKFORGE_REAL_SYSTEM_E2E, or IIS/SQL not present" -ForegroundColor DarkYellow
        Write-Host "  on this machine) — read the test's skip message in the TRX for which one." -ForegroundColor DarkYellow
    }
}

Write-Host ""
Write-Host "Reminder: this run only covers the 8 MSI-recipe real-system tests." -ForegroundColor Yellow
Write-Host "Bundle rollback, feature-picker ADDLOCAL, external containers, the turnkey UI host," -ForegroundColor Yellow
Write-Host "per-culture UI, and the Authenticode ceremony have NO automated coverage — see" -ForegroundColor Yellow
Write-Host "docs/testing/real-machine-verification.md Part 2 for the manual checklist." -ForegroundColor Yellow
Write-Host ""

exit $testExit
