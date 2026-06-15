# build-docs.ps1 — Regenerate the FalkForge HTML manual.
#
# Concatenates the 7 documentation fragments in docs/gen/ (in order) into the
# repo-root documentation.html, byte-for-byte. No BOM, no line-ending changes.
#
# Usage (from anywhere):
#   pwsh docs/gen/build-docs.ps1
# After editing any fragment, run this to regenerate documentation.html, then
# verify with:  git diff --stat documentation.html

$ErrorActionPreference = 'Stop'

$genDir = $PSScriptRoot
$outFile = [System.IO.Path]::GetFullPath((Join-Path $genDir '..\..\documentation.html'))

$fragments = @(
    'header.html',
    'section1.html',
    'section2a.html',
    'section2b.html',
    'section3.html',
    'section4.html',
    'footer.html'
)

$sb = [System.Text.StringBuilder]::new()
foreach ($name in $fragments) {
    $path = Join-Path $genDir $name
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing fragment: $path"
    }
    [void]$sb.Append([System.IO.File]::ReadAllText($path))
}

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($outFile, $sb.ToString(), $utf8NoBom)

Write-Host "Wrote $outFile ($($sb.Length) chars from $($fragments.Count) fragments)."
