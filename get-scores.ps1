$projects = Get-ChildItem -Path 'tests' -Directory | Where-Object { Test-Path "$($_.FullName)\stryker-config.json" }
$results = @()
foreach ($proj in $projects) {
    $reportDir = Get-ChildItem -Path "$($proj.FullName)\StrykerOutput" -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1
    if ($reportDir) {
        $json = Get-ChildItem -Path $reportDir.FullName -Filter 'mutation-report.json' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($json) {
            $data = Get-Content $json.FullName | ConvertFrom-Json
            $mutants = $data.files.PSObject.Properties.Value.mutants
            $killed   = ($mutants | Where-Object { $_.status -eq 'Killed'   } | Measure-Object).Count
            $survived = ($mutants | Where-Object { $_.status -eq 'Survived' } | Measure-Object).Count
            $timeout  = ($mutants | Where-Object { $_.status -eq 'Timeout'  } | Measure-Object).Count
            $total = $killed + $survived + $timeout
            if ($total -gt 0) { $score = [math]::Round(($killed + $timeout) / $total * 100, 1) } else { $score = 0 }
            $results += [PSCustomObject]@{ Project = $proj.Name; Score = "$score%"; Killed = $killed; Survived = $survived; Timeout = $timeout }
        } else {
            $results += [PSCustomObject]@{ Project = $proj.Name; Score = "no JSON report"; Killed = 0; Survived = 0; Timeout = 0 }
        }
    } else {
        $results += [PSCustomObject]@{ Project = $proj.Name; Score = "no StrykerOutput"; Killed = 0; Survived = 0; Timeout = 0 }
    }
}
$results | Sort-Object Score | Format-Table -AutoSize
