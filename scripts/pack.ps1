#!/usr/bin/env pwsh
# pack.ps1 — dotnet pack every packable FalkForge project into a local NuGet feed
#
# Packability is deny-by-default (IsPackable=false in the root Directory.Build.props);
# every shippable project opts in, so packing the solution yields exactly the shippable
# set: the fluent-API libraries, extensions, plugins, the FalkForge.Sdk MSBuild SDK,
# and the forge CLI as .NET global tool FalkForge.Tool.
#
# The output folder doubles as a LOCAL NUGET FEED — point a NuGet.config at it:
#   <add key="falkforge-local" value="<Output>" />
# This is what forge-init/template work restores against.
#
# The version comes from the single source in the root Directory.Build.props
# (VersionPrefix + VersionSuffix) unless overridden with -Version.
#
# Usage:
#   ./scripts/pack.ps1                              # -> ./artifacts/nuget
#   ./scripts/pack.ps1 -Output C:\feed              # custom feed folder
#   ./scripts/pack.ps1 -Version 0.1.0-alpha.2       # override the single-source version

param(
    [string]$Output = "",
    [string]$Version = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$slnx = Join-Path $root "FalkForge.slnx"
if ($Output -eq "") { $Output = Join-Path $root "artifacts/nuget" }

Write-Host ""
Write-Host "FalkForge Pack" -ForegroundColor Cyan
Write-Host "==============" -ForegroundColor Cyan
Write-Host "Solution : $slnx"
Write-Host "Output   : $Output"
Write-Host ("Version  : {0}" -f ($(if ($Version) { $Version } else { "single-source (Directory.Build.props)" })))
Write-Host ""

# Idempotent: clean the feed folder.
if (Test-Path $Output) { Remove-Item -Recurse -Force $Output }
New-Item -ItemType Directory -Force $Output | Out-Null

$packArgs = @($slnx, "-c", "Release", "-o", $Output)
if ($Version) { $packArgs += "-p:Version=$Version" }

dotnet pack @packArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed" }

Write-Host ""
Write-Host "Packed packages:" -ForegroundColor Green
Get-ChildItem $Output -Filter *.nupkg | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0}" -f $_.Name)
}
Write-Host ""
Write-Host ("{0} package(s) in local feed: {1}" -f (Get-ChildItem $Output -Filter *.nupkg).Count, $Output) -ForegroundColor Green
