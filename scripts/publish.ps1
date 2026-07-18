#!/usr/bin/env pwsh
# publish.ps1 — build FalkForge in Release and publish the shippable executables
#
# Publishes:
#   forge CLI                (framework-dependent, net10.0)         -> <Output>/forge
#   FalkForge.Engine         (NativeAOT, win-x64)                   -> <Output>/engine
#   FalkForge.Engine.Elevation (NativeAOT, win-x64)                 -> <Output>/engine
#
# The engine/elevation binaries are the NativeAOT bundle runtime. The bundle compiler
# embeds the published engine automatically: it resolves <repo>/artifacts/publish/engine
# (this script's output) via EngineStubLocator, or any path set in the
# FALKFORGE_ENGINE_STUB environment variable. Run this script once and every bundle
# built in the repo becomes a runnable self-extracting installer.
#
# Usage:
#   ./scripts/publish.ps1                          # -> ./artifacts/publish
#   ./scripts/publish.ps1 -Output C:\drop          # custom output folder
#   ./scripts/publish.ps1 -SkipEngine              # skip the slow NativeAOT publishes
#
# Note: this script publishes executables only — it does not create or push NuGet
# packages. For packing + publishing FalkForge's NuGet packages, see scripts/pack.ps1
# (local pack) and .github/workflows/release.yml's publish-nuget job (CI push to
# nuget.org via Trusted Publishing, the primary path as of 2026-07).

param(
    [string]$Output = "",
    [switch]$SkipEngine
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$slnx = Join-Path $root "FalkForge.slnx"
if ($Output -eq "") { $Output = Join-Path $root "artifacts/publish" }

Write-Host ""
Write-Host "FalkForge Publish" -ForegroundColor Cyan
Write-Host "=================" -ForegroundColor Cyan
Write-Host "Solution : $slnx"
Write-Host "Output   : $Output"
Write-Host ""

# Idempotent: clean the target folder.
if (Test-Path $Output) { Remove-Item -Recurse -Force $Output }
New-Item -ItemType Directory -Force $Output | Out-Null

Write-Host "Building solution (Release)..." -ForegroundColor Yellow
dotnet build $slnx -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

Write-Host "Publishing forge CLI..." -ForegroundColor Yellow
dotnet publish (Join-Path $root "src/FalkForge.Cli/FalkForge.Cli.csproj") `
    -c Release -o (Join-Path $Output "forge")
if ($LASTEXITCODE -ne 0) { throw "forge CLI publish failed" }

if (-not $SkipEngine) {
    # NativeAOT linking (Microsoft.NETCore.Native.targets) invokes vswhere.exe to locate
    # the MSVC linker; some shells don't have the VS Installer directory on PATH.
    if (-not (Get-Command vswhere.exe -ErrorAction SilentlyContinue)) {
        $vsInstaller = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
        if (Test-Path (Join-Path $vsInstaller "vswhere.exe")) { $env:PATH += ";$vsInstaller" }
    }

    Write-Host "Publishing NativeAOT engine (this takes a few minutes)..." -ForegroundColor Yellow
    dotnet publish (Join-Path $root "src/FalkForge.Engine/FalkForge.Engine.csproj") `
        -c Release -r win-x64 -o (Join-Path $Output "engine")
    if ($LASTEXITCODE -ne 0) { throw "Engine publish failed" }

    Write-Host "Publishing NativeAOT elevation companion..." -ForegroundColor Yellow
    dotnet publish (Join-Path $root "src/FalkForge.Engine.Elevation/FalkForge.Engine.Elevation.csproj") `
        -c Release -r win-x64 -o (Join-Path $Output "engine")
    if ($LASTEXITCODE -ne 0) { throw "Engine.Elevation publish failed" }
}

Write-Host ""
Write-Host "Published artifacts:" -ForegroundColor Green
Get-ChildItem -Recurse -File $Output |
    Where-Object { $_.Extension -in ".exe", ".dll" } |
    ForEach-Object { Write-Host ("  {0}  ({1:N0} KB)" -f $_.FullName.Substring($Output.Length + 1), ($_.Length / 1KB)) }
Write-Host ""
Write-Host "Done." -ForegroundColor Green
