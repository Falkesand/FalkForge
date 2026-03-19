$projects = Get-ChildItem -Path 'tests' -Directory | Where-Object { Test-Path "$($_.FullName)\stryker-config.json" }
$results = @()
foreach ($proj in $projects) {
    $outputBase = "$($proj.FullName)\StrykerOutput"
    $reportDir = Get-ChildItem -Path $outputBase -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1
    if ($reportDir) {
        $html = Get-ChildItem -Path $reportDir.FullName -Filter 'mutation-report.html' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($html) {
            $content = Get-Content $html.FullName -Raw
            # Extract the embedded JSON from the <script> tag
            if ($content -match 'const report = ({.*})\s*</script>') {
                $jsonText = $matches[1]
                try {
                    $data = $jsonText | ConvertFrom-Json
                    $mutants = $data.files.PSObject.Properties.Value.mutants
                    $killed   = ($mutants | Where-Object { $_.status -eq 'Killed'   } | Measure-Object).Count
                    $survived = ($mutants | Where-Object { $_.status -eq 'Survived' } | Measure-Object).Count
                    $timeout  = ($mutants | Where-Object { $_.status -eq 'Timeout'  } | Measure-Object).Count
                    $total = $killed + $survived + $timeout
                    if ($total -gt 0) { $score = [math]::Round(($killed + $timeout) / $total * 100, 1) } else { $score = 0 }
                    $results += [PSCustomObject]@{ Project = $proj.Name -replace 'FalkForge\.',''; Score = $score; Killed = $killed; Survived = $survived; Timeout = $timeout; Report = $reportDir.Name }
                } catch {
                    $results += [PSCustomObject]@{ Project = $proj.Name -replace 'FalkForge\.',''; Score = "parse error"; Killed = 0; Survived = 0; Timeout = 0; Report = $reportDir.Name }
                }
            } else {
                $results += [PSCustomObject]@{ Project = $proj.Name -replace 'FalkForge\.',''; Score = "no JSON in HTML"; Killed = 0; Survived = 0; Timeout = 0; Report = $reportDir.Name }
            }
        } else {
            $results += [PSCustomObject]@{ Project = $proj.Name -replace 'FalkForge\.',''; Score = "no HTML"; Killed = 0; Survived = 0; Timeout = 0; Report = $reportDir.Name }
        }
    } else {
        $results += [PSCustomObject]@{ Project = $proj.Name -replace 'FalkForge\.',''; Score = "never run"; Killed = 0; Survived = 0; Timeout = 0; Report = "" }
    }
}
$results | Sort-Object Score | Format-Table Project, Score, Killed, Survived, Timeout, Report -AutoSize
