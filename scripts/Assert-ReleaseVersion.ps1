[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Tag,
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'
if (-not $Tag.StartsWith('v', [StringComparison]::Ordinal)) {
    throw "Release tag must start with v: $Tag"
}
$project = Join-Path $RepositoryRoot 'src/FalkForge.Cli/FalkForge.Cli.csproj'
$versionOutput = & dotnet msbuild $project -nologo -getProperty:Version -p:Configuration=Release
if ($LASTEXITCODE -ne 0) {
    throw 'Could not evaluate the release version.'
}
$builtVersion = ($versionOutput -join [Environment]::NewLine).Trim()
$tagVersion = $Tag.Substring(1)
if (-not [string]::Equals($tagVersion, $builtVersion, [StringComparison]::Ordinal)) {
    throw "Release tag version '$tagVersion' differs from the evaluated package version '$builtVersion'."
}
Write-Host "Release tag matches evaluated version $builtVersion."
