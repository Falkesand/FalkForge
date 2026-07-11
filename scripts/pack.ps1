#!/usr/bin/env pwsh
# pack.ps1 — dotnet pack every packable FalkForge project into a local NuGet feed
#
# Packability is deny-by-default (IsPackable=false in the root Directory.Build.props);
# every shippable project opts in, so packing the solution yields exactly the shippable
# set: the fluent-API libraries, extensions, plugins, the FalkForge.Sdk MSBuild SDK,
# the batteries-included FalkForge meta-package (one `dotnet add package FalkForge`),
# and the forge CLI as .NET global tool FalkForge.Tool.
#
# The output folder doubles as a LOCAL NUGET FEED — point a NuGet.config at it:
#   <add key="falkforge-local" value="<Output>" />
# This is what forge-init/template work restores against.
#
# The version comes from the single source in the root Directory.Build.props
# (VersionPrefix + VersionSuffix) unless overridden with -Version.
#
# The engine ships to NuGet consumers: by default this script first publishes the NativeAOT
# FalkForge.Engine.exe + FalkForge.Engine.Elevation.exe to <repo>/artifacts/publish/engine
# (the same output scripts/publish.ps1 produces) and packs them into
# FalkForge.Engine.Runtime.win-x64 and into the FalkForge.Tool global tool. The binaries are
# never committed to git — packing always consumes a fresh publish output.
#
# Usage:
#   ./scripts/pack.ps1                              # -> ./artifacts/nuget (publishes engine first)
#   ./scripts/pack.ps1 -Output C:\feed              # custom feed folder
#   ./scripts/pack.ps1 -Version 0.1.0-alpha.2       # override the single-source version
#   ./scripts/pack.ps1 -SkipEnginePublish           # reuse existing artifacts/publish/engine
#   ./scripts/pack.ps1 -NoEngine                    # pack WITHOUT engine binaries (explicit
#                                                   # opt-out: no runtime package, engine-less
#                                                   # tool; consumers must set FALKFORGE_ENGINE_STUB)

param(
    [string]$Output = "",
    [string]$Version = "",
    [switch]$SkipEnginePublish,
    [switch]$NoEngine
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$slnx = Join-Path $root "FalkForge.slnx"
if ($Output -eq "") { $Output = Join-Path $root "artifacts/nuget" }
$engineDir = Join-Path $root "artifacts/publish/engine"

Write-Host ""
Write-Host "FalkForge Pack" -ForegroundColor Cyan
Write-Host "==============" -ForegroundColor Cyan
Write-Host "Solution : $slnx"
Write-Host "Output   : $Output"
Write-Host ("Version  : {0}" -f ($(if ($Version) { $Version } else { "single-source (Directory.Build.props)" })))
Write-Host ("Engine   : {0}" -f ($(if ($NoEngine) { "SKIPPED (-NoEngine)" } elseif ($SkipEnginePublish) { "existing $engineDir" } else { "fresh NativeAOT publish -> $engineDir" })))
Write-Host ""

if ($NoEngine) {
    Write-Host "WARNING: packing without the engine. FalkForge.Engine.Runtime.win-x64 will not be produced and FalkForge.Tool cannot build runnable bundles without FALKFORGE_ENGINE_STUB." -ForegroundColor Yellow
}
elseif (-not $SkipEnginePublish) {
    # NativeAOT linking (Microsoft.NETCore.Native.targets) invokes vswhere.exe to locate
    # the MSVC linker; some shells don't have the VS Installer directory on PATH.
    if (-not (Get-Command vswhere.exe -ErrorAction SilentlyContinue)) {
        $vsInstaller = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
        if (Test-Path (Join-Path $vsInstaller "vswhere.exe")) { $env:PATH += ";$vsInstaller" }
    }

    if (Test-Path $engineDir) { Remove-Item -Recurse -Force $engineDir }

    Write-Host "Publishing NativeAOT engine (this takes a few minutes)..." -ForegroundColor Yellow
    dotnet publish (Join-Path $root "src/FalkForge.Engine/FalkForge.Engine.csproj") `
        -c Release -r win-x64 -o $engineDir
    if ($LASTEXITCODE -ne 0) { throw "Engine publish failed" }

    Write-Host "Publishing NativeAOT elevation companion..." -ForegroundColor Yellow
    dotnet publish (Join-Path $root "src/FalkForge.Engine.Elevation/FalkForge.Engine.Elevation.csproj") `
        -c Release -r win-x64 -o $engineDir
    if ($LASTEXITCODE -ne 0) { throw "Engine.Elevation publish failed" }
}

# Idempotent: clean the feed folder.
if (Test-Path $Output) { Remove-Item -Recurse -Force $Output }
New-Item -ItemType Directory -Force $Output | Out-Null

$packArgs = @($slnx, "-c", "Release", "-o", $Output)
if ($Version) { $packArgs += "-p:Version=$Version" }
if ($NoEngine) { $packArgs += "-p:FalkForgePackEngine=false" }

dotnet pack @packArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed" }

Write-Host ""
Write-Host "Packed packages:" -ForegroundColor Green
Get-ChildItem $Output -Filter *.nupkg | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0}" -f $_.Name)
}
Write-Host ""
Write-Host ("{0} package(s) in local feed: {1}" -f (Get-ChildItem $Output -Filter *.nupkg).Count, $Output) -ForegroundColor Green
