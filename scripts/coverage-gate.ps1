#!/usr/bin/env pwsh
#
# COPY. The original lives outside this repository, in the maintainer's tool folder, so the same
# gate can run in every project. This copy exists so CI and outside contributors can run it without
# that folder. Change behaviour in the original, bump $script:ToolVersion, then copy the whole file
# here again. Running the original inside this repo prints a warning when the two versions differ.
#
<#
.SYNOPSIS
    Measures line coverage for a .NET solution three ways at once: the solution
    total, every project on its own, and the lines the current branch changed.
    Prints the result to the screen.

.DESCRIPTION
    Runs the test suite once under `dotnet-coverage`, which works with the
    Microsoft.Testing.Platform runner. The VSTest collector
    (--collect:"XPlat Code Coverage") writes nothing under that runner and fails
    silently, so it is not used here.

    Three numbers come out of one run:

      Solution total    every product assembly, test assemblies excluded.
      Per project       one row per assembly, worst first, so the weak spot is
                        the first thing on the screen.
      Branch diff       of the coverable lines this branch changed, how many a
                        test executed. This is the number that answers "is the
                        code I am about to commit tested", which the solution
                        total cannot answer: a large well-covered codebase
                        absorbs an untested new file without moving.

    The branch diff compares the merge base with the WORKING TREE, not with
    HEAD, so uncommitted work counts. That is deliberate: at the moment this
    runs before a commit, the code being committed is not in HEAD yet.

    Only coverable lines count toward the diff figure. A changed line that the
    collector never lists (a brace, a using, a field declaration, a comment) is
    left out of both halves of the fraction. Counting them would drag every
    diff number down by a constant that says nothing about testing.

.PARAMETER RepoPath
    Any path inside the repo. Defaults to the current directory.

.PARAMETER Solution
    Solution file to test. Auto-detected (.slnx preferred, then .sln) when omitted.

.PARAMETER BaseRef
    What the branch diff compares against. Auto-detected: origin/main,
    origin/master, main, master, in that order. On the base branch itself the
    script falls back to HEAD~1 and says so.

.PARAMETER MinTotal
    Floor of the acceptable band for the solution total. Default 80.

.PARAMETER MaxTotal
    Top of the acceptable band. Default 90. Above it is reported, never failed:
    more coverage is not a defect, but it is worth seeing when a number jumps.

.PARAMETER MinDiff
    Floor for branch diff coverage. Default 80.

.PARAMETER FailUnder
    Exit 1 when the solution total or the branch diff is below its floor.
    Without this the script reports and exits 0.

.PARAMETER NoBuild
    Skip the build. Pass this when a pipeline already built the solution.

.PARAMETER SkipDiff
    Skip the branch diff.

.PARAMETER Html
    Also write a ReportGenerator HTML report next to the raw data.

.PARAMETER Open
    Open the HTML report when it is written. Implies -Html.

.PARAMETER Quiet
    Suppress per-project test output. The summary still prints.

.EXAMPLE
    cov
    # Full run in the current repo, report only.

.EXAMPLE
    cov -FailUnder -NoBuild
    # What the commit pipeline runs: enforce the floors, reuse its build.

.EXAMPLE
    cov -Html -Open
    # Same numbers plus a browsable per-file report.
#>
[CmdletBinding()]
param(
    [string]$RepoPath = (Get-Location).Path,
    [string]$Solution,
    [string]$BaseRef,
    [double]$MinTotal = 80,
    [double]$MaxTotal = 90,
    [double]$MinDiff  = 80,
    [switch]$FailUnder,
    [switch]$NoBuild,
    [switch]$SkipDiff,
    [switch]$Html,
    [switch]$Open,
    [switch]$Quiet,

    # Re-report from the last collected run instead of running the tests again.
    # The solution numbers are as old as that run; the branch diff is
    # recomputed against the working tree as it is now.
    [switch]$UseExisting,

    # Assemblies kept out of every number here, matched against the assembly
    # name as a regex. This repeats what coverage.settings.xml asks the
    # collector to do, on purpose: a settings file the collector rejects is
    # reported as a warning on a run that still exits 0, so trusting it alone
    # gives a wrong total that looks like a clean pass.
    [string[]]$ExcludeAssemblies = @(
        '\.Tests$', '\.Tests\.', '\.IntegrationTests$', '\.E2E\.Tests',
        '^xunit', '^Microsoft\.Testing', '\.TestAdapter$',
        '^Moq$', '^NSubstitute$', '^FluentAssertions$', '^Shouldly$'
    )
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_COVERAGE_TELEMETRY_OPTOUT = '1'

$script:ToolDir = Split-Path -Parent $PSCommandPath

# This script is copied into repositories as scripts/coverage-gate.ps1 so CI can run it. Two copies
# drift, and a gate that drifts from the thing developers run locally is worse than no gate, because
# CI then blocks a pull request over a number nobody can reproduce. Bump this whenever behaviour
# changes, and the copies compare themselves below.
$script:ToolVersion = '1.1.0'

# ---------------------------------------------------------------- helpers

function Die([string]$msg) {
    Write-Host ''
    Write-Host "  coverage: $msg" -ForegroundColor Red
    exit 2
}

function Need([string]$exe, [string]$install) {
    if (-not (Get-Command $exe -ErrorAction SilentlyContinue)) {
        Die "$exe is not on PATH. Install it with: $install"
    }
}

function Rate([int]$covered, [int]$valid) {
    if ($valid -le 0) { return $null }
    return 100.0 * $covered / $valid
}

# Colour a rate against the band. Below floor is the only failing state.
function RateColour([double]$rate, [double]$floor, [double]$ceiling) {
    if ($rate -lt $floor)   { return 'Red' }
    if ($rate -gt $ceiling) { return 'Cyan' }
    return 'Green'
}

function Verdict([double]$rate, [double]$floor, [double]$ceiling) {
    if ($rate -lt $floor)   { return 'UNDER' }
    if ($rate -gt $ceiling) { return 'ABOVE' }
    return 'OK'
}

# Repo-relative, forward slashes, for comparing collector paths to git paths.
function ToRepoRelative([string]$path, [string]$root) {
    if ([string]::IsNullOrWhiteSpace($path)) { return $null }
    $p = $path -replace '\\', '/'
    $r = ($root -replace '\\', '/').TrimEnd('/')
    if ($p.StartsWith($r, [StringComparison]::OrdinalIgnoreCase)) {
        return $p.Substring($r.Length).TrimStart('/')
    }
    return $null   # outside the repo: SDK or package sources, not ours
}

# ---------------------------------------------------------------- locate repo

Need 'git'            'winget install Git.Git'
Need 'dotnet'         'https://dot.net'
Need 'dotnet-coverage' 'dotnet tool install --global dotnet-coverage'

$root = (& git -C $RepoPath rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $root) { Die "not inside a git repository: $RepoPath" }
$root = $root.Trim() -replace '/', '\'

if (-not $Solution) {
    $sln = Get-ChildItem -Path $root -Filter '*.slnx' -File -ErrorAction SilentlyContinue |
           Select-Object -First 1
    if (-not $sln) {
        $sln = Get-ChildItem -Path $root -Filter '*.sln' -File -ErrorAction SilentlyContinue |
               Select-Object -First 1
    }
    if (-not $sln) { Die "no .slnx or .sln found at $root. Pass -Solution." }
    $Solution = $sln.FullName
}
if (-not (Test-Path $Solution)) { Die "solution not found: $Solution" }

$branch = (& git -C $root rev-parse --abbrev-ref HEAD 2>$null)
if ($branch) { $branch = $branch.Trim() }

# Does this repo carry its own copy for CI, and is it the same version? A silent divergence here is
# the whole risk of duplicating the script, so it gets said out loud on every run.
$repoCopy = Join-Path $root 'scripts\coverage-gate.ps1'
if ((Test-Path $repoCopy) -and ($repoCopy -ne $PSCommandPath)) {
    $repoVersion = $null
    foreach ($line in (Get-Content $repoCopy -TotalCount 200 -ErrorAction SilentlyContinue)) {
        if ($line -match "^\s*\`$script:ToolVersion\s*=\s*'([^']+)'") { $repoVersion = $Matches[1]; break }
    }
    if (-not $repoVersion) {
        Write-Host "  WARNING  $repoCopy carries no `$script:ToolVersion, so drift cannot be checked." -ForegroundColor Yellow
    }
    elseif ($repoVersion -ne $script:ToolVersion) {
        Write-Host "  WARNING  this repo's CI copy is version $repoVersion, this script is $($script:ToolVersion)." -ForegroundColor Yellow
        Write-Host "           CI will not reproduce what you are about to see. Copy this file over" -ForegroundColor Yellow
        Write-Host "           scripts\coverage-gate.ps1, or run that one instead." -ForegroundColor Yellow
    }
}

# Repo-local settings win over the shared default.
$settings = Join-Path $root '.coverage.settings.xml'
if (-not (Test-Path $settings)) {
    $settings = Join-Path $script:ToolDir 'coverage.settings.xml'
}
if (-not (Test-Path $settings)) { Die "collector settings not found: $settings" }

$outDir = Join-Path $root 'TestResults\coverage'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$cobertura = Join-Path $outDir 'coverage.cobertura.xml'
$runLog    = Join-Path $outDir 'test-run.log'

# ---------------------------------------------------------------- build + collect

$slnName = Split-Path -Leaf $Solution
Write-Host ''
Write-Host "  Coverage  $slnName" -ForegroundColor White
Write-Host "  repo      $root"
Write-Host "  branch    $branch"
Write-Host "  settings  $settings"
Write-Host ''

if ($UseExisting) {
    if (-not (Test-Path $cobertura)) {
        Die "-UseExisting was given but there is no earlier report at $cobertura. Run once without it."
    }
    $age = [int]((Get-Date) - (Get-Item $cobertura).LastWriteTime).TotalMinutes
    Write-Host "  reusing the report collected $age min ago (-UseExisting)" -ForegroundColor DarkYellow
    $elapsed = '--:--'
}
else {

if (-not $NoBuild) {
    Write-Host '  building...' -ForegroundColor Yellow
    & dotnet build $Solution -v:q --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) {
        & dotnet build $Solution -v:m --nologo
        Die "build failed (exit $LASTEXITCODE). Coverage not collected."
    }
}

Write-Host '  running tests under the collector...' -ForegroundColor Yellow
$sw = [Diagnostics.Stopwatch]::StartNew()

$inner = 'dotnet test "{0}" --no-build -- --report-trx --report-trx-filename coverage-run.trx' -f $Solution

& dotnet-coverage collect -f cobertura -s $settings -o $cobertura $inner 2>&1 |
    Tee-Object -FilePath $runLog |
    ForEach-Object {
        if ($Quiet) { return }
        if ($_ -match 'Passed!|Failed!|error |Test run summary|Tests failed') {
            Write-Host "    $_"
        }
    }

$testExit = $LASTEXITCODE
$sw.Stop()
$elapsed = '{0:mm\:ss}' -f $sw.Elapsed

if ($testExit -ne 0) {
    Write-Host ''
    Write-Host '  TESTS FAILED. Coverage numbers below would be meaningless.' -ForegroundColor Red
    Write-Host "  Failing output, tail of $runLog" -ForegroundColor Red
    Get-Content $runLog -Tail 30 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    exit 1
}

if (-not (Test-Path $cobertura)) { Die "collector wrote no report to $cobertura" }

}   # end of the collect branch

# ---------------------------------------------------------------- parse cobertura
# Streamed with XmlReader rather than [xml]: a full-solution report runs to tens
# of megabytes and a DOM load of that costs hundreds of MB of RAM.

Write-Host '  reading the report...' -ForegroundColor Yellow

$fileHits = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.Dictionary[int, int]]]::new([StringComparer]::OrdinalIgnoreCase)
$filePkg  = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)

$xrs = [System.Xml.XmlReaderSettings]::new()
$xrs.IgnoreComments   = $true
$xrs.IgnoreWhitespace = $true
$reader = [System.Xml.XmlReader]::Create($cobertura, $xrs)
$lateExcluded = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

try {
    $pkg = $null
    $rel = $null
    $pkgExcluded = $false
    while ($reader.Read()) {
        if ($reader.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        switch ($reader.Name) {
            'package' {
                $pkg = $reader.GetAttribute('name')
                $rel = $null
                $pkgExcluded = $false
                foreach ($rx in $ExcludeAssemblies) {
                    if ($pkg -match $rx) {
                        $pkgExcluded = $true
                        [void]$lateExcluded.Add($pkg)
                        break
                    }
                }
            }
            'class' {
                if ($pkgExcluded) {
                    $rel = $null
                } else {
                    $rel = ToRepoRelative $reader.GetAttribute('filename') $root
                    if ($rel -and -not $filePkg.ContainsKey($rel)) { $filePkg[$rel] = $pkg }
                }
            }
            'line' {
                if ($rel) {
                    $n = 0; $h = 0
                    if ([int]::TryParse($reader.GetAttribute('number'), [ref]$n)) {
                        [void][int]::TryParse($reader.GetAttribute('hits'), [ref]$h)
                        $d = $null
                        if (-not $fileHits.TryGetValue($rel, [ref]$d)) {
                            $d = [System.Collections.Generic.Dictionary[int, int]]::new()
                            $fileHits[$rel] = $d
                        }
                        # The same line can appear under several methods
                        # (partials, generics, iterators). Keep the best hit
                        # count seen for it.
                        $prev = 0
                        if ($d.TryGetValue($n, [ref]$prev)) {
                            if ($h -gt $prev) { $d[$n] = $h }
                        } else {
                            $d[$n] = $h
                        }
                    }
                }
            }
        }
    }
}
finally { $reader.Dispose() }

# The collector was asked to leave these out and did not, which means it
# rejected the settings file and said so on a run that still exited 0.
if ($lateExcluded.Count -gt 0) {
    Write-Host ''
    Write-Host '  WARNING: the collector included assemblies the settings file excludes.' -ForegroundColor Yellow
    Write-Host "  It rejected $settings and fell back to its defaults, which it does" -ForegroundColor Yellow
    Write-Host '  without failing the run. The numbers below are still correct because they' -ForegroundColor Yellow
    Write-Host '  were filtered again here, but collection did more work than it needed to.' -ForegroundColor Yellow
    Write-Host ("  Dropped: {0}" -f (($lateExcluded | Sort-Object) -join ', ')) -ForegroundColor DarkYellow
    Write-Host '  Fix: the root element must be <Configuration> wrapping <CodeCoverage>, and' -ForegroundColor Yellow
    Write-Host '  the file must be well-formed XML (no raw < or > inside a regex).' -ForegroundColor Yellow
}

if ($fileHits.Count -eq 0) {
    Die "the report contains no source files under $root. Check the exclusions in $settings."
}

# Roll files up per package.
$pkgCovered = @{}
$pkgValid   = @{}
$totCovered = 0
$totValid   = 0

foreach ($kv in $fileHits.GetEnumerator()) {
    $rel = $kv.Key
    $p   = $filePkg[$rel]
    if (-not $p) { $p = '(unattributed)' }
    if (-not $pkgCovered.ContainsKey($p)) { $pkgCovered[$p] = 0; $pkgValid[$p] = 0 }
    foreach ($h in $kv.Value.Values) {
        $pkgValid[$p]++
        $totValid++
        if ($h -gt 0) { $pkgCovered[$p]++; $totCovered++ }
    }
}

$totalRate = Rate $totCovered $totValid

# ---------------------------------------------------------------- branch diff

$diffRate = $null; $diffCovered = 0; $diffValid = 0; $diffLabel = ''; $diffFiles = 0

if (-not $SkipDiff) {
    if (-not $BaseRef) {
        foreach ($cand in @('origin/main', 'origin/master', 'main', 'master')) {
            & git -C $root rev-parse --verify --quiet $cand 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) { $BaseRef = $cand; break }
        }
    }

    $mergeBase = $null
    if ($BaseRef) {
        $mergeBase = (& git -C $root merge-base $BaseRef HEAD 2>$null)
        if ($LASTEXITCODE -ne 0) { $mergeBase = $null } else { $mergeBase = $mergeBase.Trim() }
    }

    $headSha = (& git -C $root rev-parse HEAD 2>$null)
    if ($headSha) { $headSha = $headSha.Trim() }

    # On the base branch itself there is no branch to diff; use the last commit.
    if ($mergeBase -and $headSha -and $mergeBase -eq $headSha) {
        $mergeBase = (& git -C $root rev-parse HEAD~1 2>$null)
        if ($LASTEXITCODE -eq 0) {
            $mergeBase = $mergeBase.Trim()
            $diffLabel = "HEAD~1 -> working tree (on $BaseRef, nothing to compare a branch against)"
        } else { $mergeBase = $null }
    } elseif ($mergeBase) {
        $diffLabel = "{0} ({1}) -> working tree" -f $BaseRef, $mergeBase.Substring(0, 8)
    }

    if ($mergeBase) {
        # Two dots against the working tree, so staged and unstaged edits count.
        # At pre-commit time the code being committed is not in HEAD yet.
        $raw = & git -C $root diff --no-color --unified=0 $mergeBase -- '*.cs' 2>$null

        # `git diff` only sees tracked paths, so a brand-new .cs file that was never staged is
        # absent from the diff and contributes nothing to either half of the fraction. Verified
        # 2026-09-18 in a scratch repo: a modified file and a staged new file both appeared, an
        # untracked new file did not. That silently shrinks the denominator, which is the one way
        # this number can flatter a branch, so name the files instead of letting it pass.
        $untrackedCs = @(
            & git -C $root ls-files --others --exclude-standard -- '*.cs' 2>$null |
                ForEach-Object { $_.Trim() } |
                Where-Object { $_ }
        )

        $added = @{}   # repo-relative path -> HashSet[int]
        $cur = $null
        foreach ($line in $raw) {
            if ($line.StartsWith('+++ ')) {
                $p = $line.Substring(4).Trim()
                if ($p -eq '/dev/null') { $cur = $null; continue }
                if ($p.StartsWith('b/')) { $p = $p.Substring(2) }
                $cur = $p
                if (-not $added.ContainsKey($cur)) {
                    $added[$cur] = [System.Collections.Generic.HashSet[int]]::new()
                }
                continue
            }
            if ($cur -and $line.StartsWith('@@')) {
                if ($line -match '^@@ -\S+ \+(\d+)(?:,(\d+))? @@') {
                    $start = [int]$Matches[1]
                    $count = if ($Matches[2]) { [int]$Matches[2] } else { 1 }
                    for ($i = 0; $i -lt $count; $i++) { [void]$added[$cur].Add($start + $i) }
                }
            }
        }

        foreach ($kv in $added.GetEnumerator()) {
            $rel = $kv.Key
            $d = $null
            if (-not $fileHits.TryGetValue($rel, [ref]$d)) { continue }  # not product code, or not built
            $touched = $false
            foreach ($n in $kv.Value) {
                $h = 0
                if (-not $d.TryGetValue($n, [ref]$h)) { continue }        # changed but not coverable
                $diffValid++
                $touched = $true
                if ($h -gt 0) { $diffCovered++ }
            }
            if ($touched) { $diffFiles++ }
        }

        $diffRate = Rate $diffCovered $diffValid
    }
}

# ---------------------------------------------------------------- html report

$htmlIndex = $null
if ($Html -or $Open) {
    if (Get-Command 'reportgenerator' -ErrorAction SilentlyContinue) {
        $htmlDir = Join-Path $outDir 'html'
        # Same exclusions as the table above, in ReportGenerator's wildcard
        # syntax, so the browsable report and the screen agree.
        $asmFilter = '-assemblyfilters:-*.Tests;-*.Tests.*;-*.IntegrationTests;-xunit*;-Microsoft.Testing*'
        & reportgenerator "-reports:$cobertura" "-targetdir:$htmlDir" $asmFilter `
                          "-reporttypes:Html;JsonSummary" "-verbosity:Warning" | Out-Null
        $htmlIndex = Join-Path $htmlDir 'index.html'
        if (-not (Test-Path $htmlIndex)) { $htmlIndex = $null }
    } else {
        Write-Host '  reportgenerator not installed, skipping the HTML report.' -ForegroundColor DarkYellow
        Write-Host '  dotnet tool install --global dotnet-reportgenerator-globaltool' -ForegroundColor DarkYellow
    }
}

# ---------------------------------------------------------------- report

$rows = foreach ($p in $pkgValid.Keys) {
    $r = Rate $pkgCovered[$p] $pkgValid[$p]
    if ($null -eq $r) { continue }
    [pscustomobject]@{
        Project = $p
        Covered = $pkgCovered[$p]
        Valid   = $pkgValid[$p]
        Rate    = $r
    }
}
$rows = @($rows | Sort-Object Rate)

$nameWidth = 8
foreach ($r in $rows) { if ($r.Project.Length -gt $nameWidth) { $nameWidth = $r.Project.Length } }
if ($nameWidth -gt 52) { $nameWidth = 52 }
$ruleWidth = $nameWidth + 30

Write-Host ''
Write-Host ('  {0}  {1,9}  {2,9}  {3,7}' -f 'PROJECT'.PadRight($nameWidth), 'LINES', 'COVERED', 'RATE') -ForegroundColor White
Write-Host ('  ' + ('-' * $ruleWidth)) -ForegroundColor DarkGray

foreach ($r in $rows) {
    $name = $r.Project
    if ($name.Length -gt $nameWidth) { $name = $name.Substring(0, $nameWidth - 1) + '~' }
    Write-Host ('  {0}  {1,9:N0}  {2,9:N0}  {3,6:N1}%' -f `
                $name.PadRight($nameWidth), $r.Valid, $r.Covered, $r.Rate) `
               -ForegroundColor (RateColour $r.Rate $MinTotal $MaxTotal)
}

Write-Host ('  ' + ('-' * $ruleWidth)) -ForegroundColor DarkGray

$totColour  = RateColour $totalRate $MinTotal $MaxTotal
$totVerdict = Verdict    $totalRate $MinTotal $MaxTotal
Write-Host ('  {0}  {1,9:N0}  {2,9:N0}  {3,6:N1}%   {4}  (band {5:N0}-{6:N0}%)' -f `
            'SOLUTION TOTAL'.PadRight($nameWidth), $totValid, $totCovered, $totalRate, `
            $totVerdict, $MinTotal, $MaxTotal) -ForegroundColor $totColour

Write-Host ''
Write-Host '  Branch diff' -ForegroundColor White
if ($SkipDiff) {
    Write-Host '    skipped (-SkipDiff).' -ForegroundColor DarkGray
} elseif ($null -eq $diffRate) {
    if ($diffValid -eq 0 -and $diffLabel) {
        Write-Host "    $diffLabel" -ForegroundColor DarkGray
        Write-Host '    no coverable line changed, nothing to measure.' -ForegroundColor DarkGray
    } else {
        Write-Host '    no base branch found, so there is nothing to compare against.' -ForegroundColor DarkYellow
        Write-Host '    Pass -BaseRef <ref> to name one.' -ForegroundColor DarkYellow
    }
} else {
    $diffColour  = RateColour $diffRate $MinDiff 100
    $diffVerdict = Verdict    $diffRate $MinDiff 100
    Write-Host "    $diffLabel" -ForegroundColor DarkGray
    Write-Host ('    {0,-22} {1,9:N0}' -f 'files touched', $diffFiles)
    Write-Host ('    {0,-22} {1,9:N0}' -f 'coverable lines', $diffValid)
    Write-Host ('    {0,-22} {1,9:N0}' -f 'covered', $diffCovered)
    Write-Host ('    {0,-22} {1,8:N1}%   {2}  (floor {3:N0}%)' -f `
                'diff coverage', $diffRate, $diffVerdict, $MinDiff) -ForegroundColor $diffColour
}

# A new .cs file that was never staged is invisible to `git diff`, so none of its lines reach
# either half of the fraction above. Say which files, because the effect is to make the branch
# look better than it is.
if (-not $SkipDiff -and $untrackedCs -and $untrackedCs.Count -gt 0) {
    Write-Host ''
    Write-Host ('    NOT COUNTED: {0} untracked .cs file{1}. git diff only sees tracked paths.' -f `
                $untrackedCs.Count, $(if ($untrackedCs.Count -eq 1) { '' } else { 's' })) `
               -ForegroundColor Yellow
    foreach ($f in ($untrackedCs | Sort-Object | Select-Object -First 10)) {
        Write-Host "      $f" -ForegroundColor Yellow
    }
    if ($untrackedCs.Count -gt 10) {
        Write-Host ('      ... and {0} more' -f ($untrackedCs.Count - 10)) -ForegroundColor Yellow
    }
    Write-Host '    git add them and re-run to include them in the branch diff.' -ForegroundColor Yellow
}

Write-Host ''
Write-Host ('  tests+collection took {0}' -f $elapsed) -ForegroundColor DarkGray
Write-Host ("  raw report  $cobertura") -ForegroundColor DarkGray
if ($htmlIndex) { Write-Host ("  html report $htmlIndex") -ForegroundColor DarkGray }

# Machine-readable, for anything that wants to trend these later.
$summary = [ordered]@{
    timestamp     = (Get-Date).ToUniversalTime().ToString('o')
    solution      = $slnName
    branch        = $branch
    baseRef       = $BaseRef
    totalRate     = [math]::Round($totalRate, 2)
    totalCovered  = $totCovered
    totalValid    = $totValid
    diffRate      = if ($null -ne $diffRate) { [math]::Round($diffRate, 2) } else { $null }
    diffCovered   = $diffCovered
    diffValid     = $diffValid
    untrackedCs   = @($untrackedCs)
    minTotal      = $MinTotal
    maxTotal      = $MaxTotal
    minDiff       = $MinDiff
    projects      = @($rows | ForEach-Object {
                        [ordered]@{ name = $_.Project; rate = [math]::Round($_.Rate, 2)
                                    covered = $_.Covered; valid = $_.Valid } })
}
$summary | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $outDir 'summary.json') -Encoding utf8

if ($Open -and $htmlIndex) { Start-Process $htmlIndex }

# ---------------------------------------------------------------- exit

$failures = @()
if ($totalRate -lt $MinTotal) {
    $failures += ('solution total {0:N1}% is below the {1:N0}% floor' -f $totalRate, $MinTotal)
}
if ($null -ne $diffRate -and $diffRate -lt $MinDiff) {
    $failures += ('branch diff {0:N1}% is below the {1:N0}% floor' -f $diffRate, $MinDiff)
}

Write-Host ''
if ($failures.Count -eq 0) {
    Write-Host '  Coverage OK.' -ForegroundColor Green
    exit 0
}

foreach ($f in $failures) { Write-Host "  BELOW FLOOR: $f" -ForegroundColor Red }
if ($FailUnder) {
    Write-Host '  Exiting 1 because -FailUnder was given.' -ForegroundColor Red
    exit 1
}
Write-Host '  Reporting only. Pass -FailUnder to make this fail the run.' -ForegroundColor DarkYellow
exit 0
