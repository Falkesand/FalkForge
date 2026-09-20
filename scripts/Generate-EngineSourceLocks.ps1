param(
    [Parameter(Mandatory)][string]$SourceRoot,
    [Parameter(Mandatory)][string]$Feed,
    [Parameter(Mandatory)][string]$RepositoryRoot
)
$ErrorActionPreference = 'Stop'
$SourceRoot = [IO.Path]::GetFullPath($SourceRoot)
$Feed = [IO.Path]::GetFullPath($Feed)
if (-not (Test-Path -LiteralPath $Feed -PathType Container)) {
    throw "Pack sibling libraries first, then set FalkForgeSourceLockFeed to their local feed."
}
# Use a fresh cache for FalkForge packages: same-version development builds must not
# reuse older package bytes from a developer's global cache.
$packageCache = Join-Path $SourceRoot ('lock-packages-' + [Guid]::NewGuid().ToString('N'))
$configPath = Join-Path $SourceRoot 'LockGeneration.NuGet.config'
$escapedFeed = [System.Security.SecurityElement]::Escape($Feed)
@"
<configuration>
  <packageSources><clear /><add key="local" value="$escapedFeed" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="FalkForge.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $configPath -Encoding utf8

# Non-FalkForge inputs must already have a reviewed version/hash in this checkout.
$approved = @{}
foreach ($area in @('src', 'tests')) {
    Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot $area) -Filter packages.lock.json -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } |
        ForEach-Object {
            $locked = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json -AsHashtable
            foreach ($graph in $locked.dependencies.Values) {
                foreach ($entry in $graph.GetEnumerator()) {
                    if ($entry.Value.contentHash) {
                        $approved["$($entry.Key)/$($entry.Value.resolved)/$($entry.Value.contentHash)"] = $true
                    }
                }
            }
        }
}
Push-Location -LiteralPath $SourceRoot
try {
    foreach ($name in @('FalkForge.Engine', 'FalkForge.Engine.Elevation')) {
        $project = Join-Path $SourceRoot "$name/$name.csproj"
        dotnet restore $project --force-evaluate --configfile $configPath --packages $packageCache -p:RestoreLockedMode=false --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw "Source lock generation failed for $name." }
        $lockPath = Join-Path $SourceRoot "$name/packages.lock.json"
        $locked = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json -AsHashtable
        foreach ($graph in $locked.dependencies.Values) {
            foreach ($entry in $graph.GetEnumerator()) {
                if ($entry.Key -notlike 'FalkForge.*' -and $entry.Value.contentHash) {
                    $identity = "$($entry.Key)/$($entry.Value.resolved)/$($entry.Value.contentHash)"
                    if (-not $approved.ContainsKey($identity)) {
                        throw "Unreviewed packaged-source dependency: $($entry.Key) $($entry.Value.resolved). Update the repository lock files deliberately before packing."
                    }
                }
            }
        }
    }
} finally {
    Pop-Location
}
