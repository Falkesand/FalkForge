# provability.ps1 — demonstrates forge verify and forge plan-diff on a reproducible MSI.
#
# Run from the repo root:
#   pwsh demo/56-verify-and-plan/provability.ps1
#
# Prerequisites: .NET 10 SDK, FalkForge CLI buildable from source.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$DemoDir  = $PSScriptRoot
$CliProj  = "$DemoDir/../../src/FalkForge.Cli/FalkForge.Cli.csproj"
$DemoProj = "$DemoDir/56-verify-and-plan.csproj"
$OutDir   = "$DemoDir/out"

# Pin SOURCE_DATE_EPOCH for reproducible builds.
# forge verify --rebuild sets this automatically; we set it here too so the
# manual build steps in this script produce the same bytes as the verify rebuild.
# Using a fixed value means any run of this script with the same source produces
# an identical MSI — that is the point of reproducible builds.
if (-not $env:SOURCE_DATE_EPOCH) {
    $env:SOURCE_DATE_EPOCH = "1700000000"
}

function Invoke-Forge {
    param([string[]]$Args)
    dotnet run --project $CliProj -- @Args
    if ($LASTEXITCODE -ne 0) { throw "forge $($Args[0]) exited $LASTEXITCODE" }
}

# ---------------------------------------------------------------------------
# Step 1: Build the MSI (v1)
#
# The build must run from the demo directory so that relative payload paths
# (payload/readme.txt) resolve correctly. We change to $DemoDir first.
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Step 1: Build v1 artifact ===" -ForegroundColor Cyan

Push-Location $DemoDir
try {
    $V1Dir = "$OutDir/v1"
    dotnet run --project $DemoProj -- -o $V1Dir
    if ($LASTEXITCODE -ne 0) { throw "Build v1 failed" }
} finally { Pop-Location }
$V1Msi = Get-ChildItem "$V1Dir" -Filter "*.msi" -Recurse | Select-Object -First 1 -ExpandProperty FullName
Write-Host "Built: $V1Msi"

# ---------------------------------------------------------------------------
# Step 2: forge verify --rebuild
#
# Rebuilds the project from source and compares the output byte-for-byte
# against the artifact you just built. Because Reproducible() is set,
# the two builds should be identical and the verdict is VERIFIED.
#
# If you sign the MSI after building it (e.g. with signtool), the signature
# bytes differ and you will see MISMATCH instead of VERIFIED. That is
# expected and correct — the verify command reports the signed status.
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Step 2: forge verify --rebuild ===" -ForegroundColor Cyan
Write-Host "  Rebuilds the project and compares output against the artifact."
Write-Host "  Expected verdict: VERIFIED (byte-identical reproducible build)"
Write-Host ""

Invoke-Forge @("verify", $V1Msi, "--rebuild", $DemoProj)

# ---------------------------------------------------------------------------
# Step 3: Build a second artifact (simulating a v2 release)
#
# For plan-diff to show something interesting, change a file or version.
# Here we just rebuild — since the source did not change, plan-diff will
# report 'No differences found'. In a real workflow you would edit Program.cs
# (e.g. bump Version to 2.0.0) between builds.
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Step 3: Build v2 artifact ===" -ForegroundColor Cyan

Push-Location $DemoDir
try {
    $V2Dir = "$OutDir/v2"
    dotnet run --project $DemoProj -- -o $V2Dir
    if ($LASTEXITCODE -ne 0) { throw "Build v2 failed" }
} finally { Pop-Location }
$V2Msi = Get-ChildItem "$V2Dir" -Filter "*.msi" -Recurse | Select-Object -First 1 -ExpandProperty FullName
Write-Host "Built: $V2Msi"

# ---------------------------------------------------------------------------
# Step 4: forge plan-diff <old> <new>
#
# Compares two MSI artifacts and reports what changed: packages, features,
# services, registry entries, files, shortcuts, upgrade entries.
#
# When source is identical the diff reports 'No changes detected'.
# In a real workflow where you bumped the version or added a file, this
# command shows exactly what changed before you ship the update.
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Step 4: forge plan-diff v1 -> v2 ===" -ForegroundColor Cyan
Write-Host "  Compares two artifacts and reports what changed."
Write-Host "  Expected: 'No changes detected' (same source, same output)"
Write-Host ""

Invoke-Forge @("plan-diff", $V1Msi, $V2Msi)

# ---------------------------------------------------------------------------
# Step 5: forge plan  (bundle EXE only — informational)
#
# forge plan reads the embedded manifest from a bundle EXE and launches the
# FalkForge Engine in headless plan-only mode to compute the install plan.
# It requires:
#   1. A compiled bundle EXE (not an MSI)
#   2. The published Engine binary (FalkForge.Engine.exe) on PATH or next to the CLI
#
# This demo produces an MSI, not a bundle, so forge plan is not demonstrated
# here. See demo/35-bundle-simple for the bundle project shape, then run:
#
#   forge plan path/to/YourBundle.exe
#
# to see what actions the installer would take before you run it.
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Step 5: forge plan (bundle EXE only — not run here) ===" -ForegroundColor Yellow
Write-Host "  forge plan requires a compiled bundle EXE + Engine binary."
Write-Host "  This demo produces an MSI. See demo/35-bundle-simple for a bundle."
Write-Host "  Command: forge plan path/to/YourBundle.exe"
Write-Host ""

Write-Host "=== Done ===" -ForegroundColor Green
