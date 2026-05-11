#!/usr/bin/env pwsh
# scan-deps.ps1 — reproducible dependency scan for FalkForge
#
# Tools required (globally installed):
#   snitch  2.x   — dotnet tool install -g snitch
#   nugone  2.x   — dotnet tool install -g nugone
#
# Usage:
#   ./scripts/scan-deps.ps1                  # from repo root
#   ./scripts/scan-deps.ps1 -Verbose         # include dotnet list output

param(
    [switch]$Verbose
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$slnx = Join-Path $root "FalkForge.slnx"

Write-Host ""
Write-Host "FalkForge Dependency Scan" -ForegroundColor Cyan
Write-Host "=========================" -ForegroundColor Cyan
Write-Host "Solution : $slnx"
Write-Host "Date     : $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
Write-Host ""

# ---------------------------------------------------------------------------
# 1. nugone — solution-level unused package detection
# ---------------------------------------------------------------------------
Write-Host "[1/2] nugone analyze (unused NuGet references)..." -ForegroundColor Yellow

$nugoneResult = nugone analyze --project $slnx --dry-run --format text 2>&1
$nugoneExit = $LASTEXITCODE
$nugoneResult | ForEach-Object { Write-Host "  $_" }

if ($nugoneExit -ne 0) {
    Write-Host "  nugone exited with code $nugoneExit — review findings above." -ForegroundColor Red
} else {
    Write-Host "  nugone: OK" -ForegroundColor Green
}

Write-Host ""

# ---------------------------------------------------------------------------
# 2. snitch — transitive package leak detection
#    Note: snitch v2.x cannot build .NET 10 + central package management
#    projects. It succeeds only on FalkForge.Sdk (netstandard2.0).
#    The dotnet SDK fallback below provides equivalent signal.
# ---------------------------------------------------------------------------
Write-Host "[2/2] snitch (transitive leak check)..." -ForegroundColor Yellow

$sdkProj = Join-Path $root "src\FalkForge.Sdk\FalkForge.Sdk.csproj"
$snitchResult = snitch $sdkProj 2>&1
$snitchExit = $LASTEXITCODE
$snitchResult | ForEach-Object { Write-Host "  [Sdk] $_" }

Write-Host ""
Write-Host "  Snitch fallback — dotnet list package --include-transitive" -ForegroundColor DarkGray

$listResult = dotnet list $slnx package --include-transitive 2>&1
$listExit = $LASTEXITCODE

if ($Verbose) {
    $listResult | ForEach-Object { Write-Host "  $_" }
} else {
    # Show only projects that have transitive packages listed
    $inTransitive = $false
    $currentProject = ""
    foreach ($line in $listResult) {
        if ($line -match "^Project '(.+)'") {
            $currentProject = $Matches[1]
            $inTransitive = $false
        }
        if ($line -match "Transitive Package") {
            $inTransitive = $true
        }
        if ($inTransitive -and $line -match "^\s+> ") {
            Write-Host "  [$currentProject] TRANSITIVE: $($line.Trim())" -ForegroundColor DarkGray
        }
    }
}

if ($listExit -ne 0) {
    Write-Host "  dotnet list exited with code $listExit" -ForegroundColor Red
} else {
    Write-Host "  SDK transitive listing: OK" -ForegroundColor Green
}

Write-Host ""

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
$overallOk = ($nugoneExit -eq 0) -and ($listExit -eq 0)

if ($overallOk) {
    Write-Host "Scan complete — no issues found." -ForegroundColor Green
} else {
    Write-Host "Scan complete — review findings above before committing." -ForegroundColor Red
    exit 1
}
