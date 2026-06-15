#Requires -Version 7
<#
.SYNOPSIS
    Demonstrates the full `forge migrate` round-trip:
      1. Build the sample installer → sample.msi
      2. Run `forge migrate` to convert sample.msi into a FalkForge C# project
      3. Build the generated project to prove it compiles and produces a new MSI

.DESCRIPTION
    Run from the demo/54-forge-migrate/ folder:
        pwsh migrate.ps1

    Prerequisites:
        - .NET 10 SDK
        - Windows (MSI migration uses msi.dll)
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$DemoDir  = $PSScriptRoot
$RepoRoot = Resolve-Path (Join-Path $DemoDir '../..')
$CliProj  = Join-Path $RepoRoot 'src/FalkForge.Cli/FalkForge.Cli.csproj'
$SrcDir   = Join-Path $RepoRoot 'src'
$SampleMsi = Join-Path $DemoDir 'sample.msi'
$MigratedDir = Join-Path $DemoDir 'migrated'

# ---------------------------------------------------------------------------
# Step 1 — Build the sample installer to produce sample.msi
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Step 1: Build sample installer ===" -ForegroundColor Cyan

dotnet run `
    --project "$DemoDir/54-forge-migrate.csproj" `
    --configuration Release `
    -- "$SampleMsi"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Sample installer build failed (exit $LASTEXITCODE)."
}

Write-Host "  -> $SampleMsi" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Step 2 — Run `forge migrate` on sample.msi
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Step 2: forge migrate ===" -ForegroundColor Cyan
Write-Host "    forge migrate sample.msi -o migrated --falkforge-src ../../src"
Write-Host ""

# Remove any previous run
if (Test-Path $MigratedDir) { Remove-Item $MigratedDir -Recurse -Force }

dotnet run `
    --project "$CliProj" `
    --configuration Release `
    -- migrate "$SampleMsi" -o "$MigratedDir" --falkforge-src "$SrcDir"

if ($LASTEXITCODE -ne 0) {
    Write-Error "forge migrate failed (exit $LASTEXITCODE)."
}

Write-Host ""
Write-Host "  Generated project layout:" -ForegroundColor Green
Get-ChildItem $MigratedDir -Recurse | ForEach-Object {
    $rel = $_.FullName.Substring($MigratedDir.Length + 1)
    Write-Host "    $rel"
}

# ---------------------------------------------------------------------------
# Step 3 — Build the generated project to verify the round-trip
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Step 3: Build generated project ===" -ForegroundColor Cyan

$GeneratedCsproj = Get-ChildItem $MigratedDir -Filter '*.csproj' -Recurse | Select-Object -First 1
if (-not $GeneratedCsproj) {
    Write-Error "No .csproj found under $MigratedDir — migration may have failed."
}

dotnet build "$($GeneratedCsproj.FullName)" --configuration Release

if ($LASTEXITCODE -ne 0) {
    Write-Error "Generated project build failed (exit $LASTEXITCODE)."
}

Write-Host ""
Write-Host "Round-trip complete. The migrated project compiled successfully." -ForegroundColor Green
Write-Host "Review MIGRATION-REPORT.md in the generated project for details on what was mapped."
Write-Host ""
Write-Host "Tip: to keep the generated project, copy it out of the demo folder."
Write-Host "     The 'migrated/' directory is excluded from git (.gitignore)."
