#!/usr/bin/env pwsh
# pack-docs.ps1 — package the FalkForge documentation site into a downloadable zip
#
# Produces artifacts/falkforge-docs-<version>.zip containing exactly the static HTML
# documentation surface — nothing else. No source markdown, no internal plans or
# design docs:
#   documentation.html      — the self-contained manual (dark/light theme, client-side
#                              search, no external dependencies) — also duplicated as
#                              index.html so the zip opens directly as a browsable
#                              mini-site once extracted.
#   docs/tutorials/*.html   — narrative, demo-by-demo tutorials, plus the shared/
#                              CSS and JS they depend on.
#
# This is the same content published to GitHub Pages (see .github/workflows/pages.yml)
# and is meant to be attached to GitHub Releases as a downloadable offline doc bundle
# (see .github/workflows/release.yml).
#
# The version comes from the single source in the root Directory.Build.props
# (VersionPrefix + VersionSuffix) unless overridden with -Version, matching pack.ps1.
#
# Usage:
#   ./scripts/pack-docs.ps1                        # -> ./artifacts/falkforge-docs-<version>.zip
#   ./scripts/pack-docs.ps1 -Version 0.1.0-alpha.2  # override the single-source version
#   ./scripts/pack-docs.ps1 -Output C:\out          # custom output folder

param(
    [string]$Version = "",
    [string]$Output = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
if ($Output -eq "") { $Output = Join-Path $root "artifacts" }

if ($Version -eq "") {
    # Single-source version: see Directory.Build.props header comment and
    # scripts/README.md "Single-source version" section.
    [xml]$buildProps = Get-Content (Join-Path $root "Directory.Build.props")
    $versionPrefix = ($buildProps.Project.PropertyGroup | Where-Object { $_.VersionPrefix } | Select-Object -First 1).VersionPrefix
    $versionSuffix = ($buildProps.Project.PropertyGroup | Where-Object { $_.VersionSuffix } | Select-Object -First 1).VersionSuffix
    $Version = if ($versionSuffix) { "$versionPrefix-$versionSuffix" } else { $versionPrefix }
}

$docHtml = Join-Path $root "documentation.html"
$tutorialsDir = Join-Path $root "docs/tutorials"

if (-not (Test-Path $docHtml)) { throw "documentation.html not found at $docHtml" }
if (-not (Test-Path $tutorialsDir)) { throw "docs/tutorials not found at $tutorialsDir" }

Write-Host ""
Write-Host "FalkForge Docs Pack" -ForegroundColor Cyan
Write-Host "====================" -ForegroundColor Cyan
Write-Host "Version : $Version"
Write-Host "Output  : $Output"
Write-Host ""

$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("falkforge-docs-" + [System.Guid]::NewGuid())
New-Item -ItemType Directory -Force $staging | Out-Null

try {
    # documentation.html ships twice: under its own name (matches in-repo links like
    # [documentation.html](documentation.html)) and as index.html so the zip is a
    # browsable mini-site the moment it's extracted.
    Copy-Item $docHtml (Join-Path $staging "documentation.html")
    Copy-Item $docHtml (Join-Path $staging "index.html")

    # docs/tutorials/*.html + shared/ assets only — no markdown, no plans/design docs.
    $stagingTutorials = Join-Path $staging "docs/tutorials"
    New-Item -ItemType Directory -Force $stagingTutorials | Out-Null
    Copy-Item (Join-Path $tutorialsDir "*.html") $stagingTutorials
    $sharedDir = Join-Path $tutorialsDir "shared"
    if (Test-Path $sharedDir) {
        Copy-Item $sharedDir (Join-Path $stagingTutorials "shared") -Recurse
    }

    if (-not (Test-Path $Output)) { New-Item -ItemType Directory -Force $Output | Out-Null }
    $zipPath = Join-Path $Output "falkforge-docs-$Version.zip"
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

    Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $zipPath -CompressionLevel Optimal

    Write-Host "Docs zip contents:" -ForegroundColor Green
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $zip.Entries | Sort-Object FullName | ForEach-Object {
            Write-Host ("  {0}  ({1:N0} bytes)" -f $_.FullName, $_.Length)
        }
    }
    finally {
        $zip.Dispose()
    }

    Write-Host ""
    Write-Host "Packed: $zipPath" -ForegroundColor Green
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
