Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CaptureScreens/CaptureScreens.psm1') -Force
$fixtureRoot = Join-Path (Split-Path -Parent $PSScriptRoot) ('bin/.cache/capture-all-tests/' + [guid]::NewGuid().ToString('N'))
$passed = 0
function Assert-AllEqual {
    param($Actual, $Expected, [string] $Name)
    if ($Actual -cne $Expected) { throw "$Name expected '$Expected', received '$Actual'." }
    $script:passed++
}
function New-AllPlan {
    param([string[]] $Scenes)
    $points = @()
    foreach ($size in @('thin', 'half', 'full', 'short')) {
        foreach ($zoom in @(100, 150)) {
            foreach ($theme in @('light', 'dark')) {
                $points += @{ size = $size; zoom = $zoom; theme = $theme }
            }
        }
    }
    return @{ scenes = $Scenes; sizes = @('thin', 'half', 'full', 'short') | ForEach-Object {
        @{ name = $_; width = 1; height = 1 }
    }; points = $points }
}
function Write-AllFixture {
    param([string] $Output, $Plan, [string[]] $Scenes, [switch] $Finding)
    $matrix = Join-Path $Output 'matrix'
    [IO.Directory]::CreateDirectory($matrix) | Out-Null
    $png = [Convert]::FromBase64String(
        'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p68AAAAASUVORK5CYII=')
    $rows = @()
    $rawFinding = $null
    foreach ($scene in $Scenes) {
        foreach ($point in $Plan.points) {
            $file = "$scene-$($point.size)-$($point.zoom)-$($point.theme).png"
            [IO.File]::WriteAllBytes((Join-Path $matrix $file), $png)
            $count = 0
            if ($Finding -and $null -eq $rawFinding) {
                $count = 1
                $rawFinding = '{"file":"' + $file + '","scene":"' + $scene +
                    '","size":"thin","zoom":100,"theme":"light","check":"sample","control":{"type":"Button","name":null},"bounds":{"x":0.1234567890123456789,"y":-0.0,"width":1E+0,"height":1},"detail":"unanticipated","expected":false,"identity":"sample","relatedControl":null,"count":1,"extra":{"large":9007199254740993}}'
            }
            $rows += [pscustomobject]@{ file = $file; scene = $scene; size = $point.size; width = 1
                height = 1; zoom = $point.zoom; theme = $point.theme; findings = $count }
        }
    }
    $rows | Export-Csv -LiteralPath (Join-Path $matrix 'index.csv') -NoTypeInformation
    [IO.File]::WriteAllText((Join-Path $matrix 'findings.json'), '[' + $rawFinding + ']')
    return $rawFinding
}
function Write-AllProposalsFixture {
    param([string] $Output)
    $prefix = 'proposals-workspace-automatic-after'
    foreach ($size in @('thin640x600', 'half960x780', 'full1920x1080', 'short960x600')) {
        foreach ($theme in @('light', 'dark')) {
            foreach ($zoom in @(100, 150)) {
                [IO.File]::WriteAllText((Join-Path $Output "$prefix-$size-$theme-$zoom.png"), 'capture')
            }
        }
    }
    [IO.File]::WriteAllText((Join-Path $Output "$prefix-states.txt"), 'states')
}
try {
    $scenes = @(1..21 | ForEach-Object { "scene-$_" })
    $batches = @(Split-MotifCaptureScenes -Scenes $scenes)
    Assert-AllEqual $batches.Count 3 '21 scenes produce three batches'
    Assert-AllEqual (($batches | ForEach-Object { $_.Scenes.Count }) -join ',') '7,7,7' 'bounded batches'
    $batches = @(Split-MotifCaptureScenes -Scenes ($scenes + 'scene-22'))
    Assert-AllEqual (($batches | ForEach-Object { $_.Scenes.Count }) -join ',') '7,7,7,1' 'future scene adds a batch'
    $output = Join-Path $fixtureRoot 'complete'
    $plan = New-AllPlan $scenes
    [IO.Directory]::CreateDirectory((Join-Path $output 'matrix')) | Out-Null
    $plan | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'matrix/capture-plan.json')
    $catalog = Read-MotifCapturePlan -OutputDirectory $output
    $batches = @(Split-MotifCaptureScenes -Scenes $catalog.scenes)
    $originalFinding = $null
    foreach ($batch in $batches) {
        $raw = Write-AllFixture $output $plan $batch.Scenes -Finding:($batch.Number -eq 1)
        if ($raw) { $originalFinding = $raw }
        Save-MotifCaptureBatch -OutputDirectory $output -Batch $batch -ExitCode $(if ($raw) { 1 } else { 0 })
    }
    $result = Merge-MotifCaptureBatches -OutputDirectory $output -Plan $catalog -Batches $batches
    Assert-AllEqual $result.complete $true 'all 336 cells complete despite a detector failure'
    Assert-AllEqual $result.captured.Count 336 'all unique cells preserved'
    Assert-AllEqual $result.absent.Count 0 'no cells absent'
    Assert-AllEqual $result.unexpectedFindings 1 'unexpected finding survives'
    Assert-AllEqual $result.success $false 'detector failure remains nonzero'
    $json = [System.Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText((Join-Path $output 'matrix/findings.json')))
    try { Assert-AllEqual $json.RootElement[0].GetRawText() $originalFinding 'raw JSON fields and numbers preserved' }
    finally { $json.Dispose() }
    Assert-AllEqual @(Get-ChildItem (Join-Path $output 'matrix/batches') -Recurse -Filter '*.png').Count 0 'snapshots contain no duplicate PNGs'
    $ordinary = Get-MotifCaptureEnvironment -OutputDirectory $output
    Assert-AllEqual $ordinary.MOTIF_CAPTURE_MATRIX $null 'ordinary matrix environment unchanged'
    $calls = [Collections.Generic.List[object]]::new()
    $runner = {
        param($Filter, $Log)
        $calls.Add(@{ filter = $Filter; scenes = $env:MOTIF_CAPTURE_SCENES; mode = $env:MOTIF_CAPTURE_MATRIX
            stage = $env:MOTIF_PROPOSALS_CAPTURE_STAGE })
        if ($Filter -like '*WriteCaptureMatrixPlan') {
            [IO.Directory]::CreateDirectory((Join-Path $env:MOTIF_SCREENSHOTS 'matrix')) | Out-Null
            $plan | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $env:MOTIF_SCREENSHOTS 'matrix/capture-plan.json')
            return 0
        }
        if ($Filter -like '*CaptureScenesAtEveryMatrixSizeAndWriteAllFindings') {
            Assert-AllEqual $env:MOTIF_CAPTURE_MATRIX '' 'batch clears all mode'
            Assert-AllEqual $env:MOTIF_CAPTURE_SIZES 'thin,half,full,short' 'batch selects all sizes'
            Assert-AllEqual $env:MOTIF_CAPTURE_ZOOMS '100,150' 'batch selects both zooms'
            Assert-AllEqual $env:MOTIF_CAPTURE_THEMES 'light,dark' 'batch selects both themes'
            Assert-AllEqual (Test-Path (Join-Path $env:MOTIF_SCREENSHOTS 'matrix/index.csv')) $false 'live index is fresh'
            Assert-AllEqual (Test-Path (Join-Path $env:MOTIF_SCREENSHOTS 'matrix/findings.json')) $false 'live findings are fresh'
            $selected = $env:MOTIF_CAPTURE_SCENES -split ','
            Write-AllFixture $env:MOTIF_SCREENSHOTS $plan $selected | Out-Null
            return 0
        }
        [IO.File]::WriteAllBytes((Join-Path $env:MOTIF_SCREENSHOTS 'other.png'), [Convert]::FromBase64String(
            'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p68AAAAASUVORK5CYII='))
        Write-AllProposalsFixture $env:MOTIF_SCREENSHOTS
        return 0
    }
    $orchestrated = Join-Path $fixtureRoot 'orchestrated'
    $savedStage = Set-MotifCaptureEnvironment @{ MOTIF_PROPOSALS_CAPTURE_STAGE = 'before' }
    try {
        $result = Invoke-MotifAllCaptures -OutputDirectory $orchestrated -RunTests $runner
        Assert-AllEqual $env:MOTIF_PROPOSALS_CAPTURE_STAGE 'before' 'caller stage restored after full capture'
    }
    finally { Restore-MotifCaptureEnvironment $savedStage }
    Assert-AllEqual $result.success $true 'fresh sequential orchestration succeeds'
    Assert-AllEqual $calls.Count 5 'catalog once, other classes once, three matrix processes'
    Assert-AllEqual $calls[1].filter 'FullyQualifiedName~Screenshot&FullyQualifiedName!~SizeMatrixScreenshots' 'other screenshot filter excludes the whole matrix class'
    Assert-AllEqual $calls[1].stage 'after' 'full capture selects a Proposals stage'
    Assert-AllEqual (($calls | Select-Object -Skip 2 | ForEach-Object { ($_.scenes -split ',').Count }) -join ',') '7,7,7' 'runner sees bounded batches'
    $trxDirectory = Join-Path $fixtureRoot 'trx'
    [IO.Directory]::CreateDirectory($trxDirectory) | Out-Null
    [IO.File]::WriteAllText((Join-Path $trxDirectory 'screenshots.trx'), @'
<TestRun><Results>
  <UnitTestResult testName="AnotherScreenshot" outcome="NotExecuted"><Output><ErrorInfo><Message>Set MOTIF_ANOTHER_STAGE before capture.</Message></ErrorInfo></Output></UnitTestResult>
  <UnitTestResult testName="OptionalProject" outcome="NotExecuted"><Output><ErrorInfo><Message>Set a project path.</Message></ErrorInfo></Output></UnitTestResult>
  <UnitTestResult testName="PassedScreenshot" outcome="Passed"><Output><ErrorInfo><Message>Set MOTIF_PASSED_STAGE.</Message></ErrorInfo></Output></UnitTestResult>
</Results></TestRun>
'@)
    $stageSkips = @(Get-MotifCaptureStageSkips $trxDirectory)
    Assert-AllEqual $stageSkips.Count 1 'only missing-stage skips are reported'
    Assert-AllEqual ($stageSkips[0] -like 'AnotherScreenshot:*MOTIF_ANOTHER_STAGE*') $true 'skip names its test and stage'
    $skippingRunner = {
        param($Filter, $Log)
        $code = & $runner $Filter $Log
        if ($Filter -like '*!~SizeMatrixScreenshots') { return @{ exitCode = $code; stageSkips = $stageSkips } }
        return $code
    }
    $skipOutput = Join-Path $fixtureRoot 'stage-skip'
    $result = Invoke-MotifAllCaptures $skipOutput $skippingRunner
    Assert-AllEqual $result.complete $false 'missing-stage skip makes an otherwise complete capture incomplete'
    Assert-AllEqual $result.success $false 'missing-stage skip fails the capture'
    Assert-AllEqual (($result.issues -join ' ') -like '*AnotherScreenshot*MOTIF_ANOTHER_STAGE*') $true 'completeness explains skipped stage'
    $matrixSkippingRunner = {
        param($Filter, $Log)
        $code = & $runner $Filter $Log
        if ($Filter -like '*CaptureScenesAtEveryMatrixSizeAndWriteAllFindings') {
            return @{ exitCode = $code; stageSkips = @('MatrixScreenshot: missing stage') }
        }
        return $code
    }
    $result = Invoke-MotifAllCaptures (Join-Path $fixtureRoot 'matrix-stage-skip') $matrixSkippingRunner
    Assert-AllEqual $result.complete $false 'a matrix batch missing-stage skip makes capture incomplete'
    Assert-AllEqual (($result.issues -join ' ') -like '*MatrixScreenshot: missing stage*') $true 'matrix skip is identified'
    $missingRunner = {
        param($Filter, $Log)
        $code = & $runner $Filter $Log
        if ($Filter -like '*!~SizeMatrixScreenshots') {
            Remove-Item (Join-Path $env:MOTIF_SCREENSHOTS 'proposals-workspace-automatic-after-thin640x600-light-100.png')
        }
        return $code
    }
    $result = Invoke-MotifAllCaptures (Join-Path $fixtureRoot 'missing-proposals') $missingRunner
    Assert-AllEqual $result.complete $false 'missing Proposals image makes capture incomplete'
    Assert-AllEqual (($result.issues -join ' ') -like '*Missing Proposals capture*') $true 'missing capture is identified'
    New-MotifCaptureSheet -OutputDirectory $orchestrated | Out-Null
    $html = [IO.File]::ReadAllText((Join-Path $orchestrated 'index.html'))
    Assert-AllEqual ([regex]::Matches($html, 'src="other.png"').Count) 1 'other photo appears once'
    Assert-AllEqual ([regex]::Matches($html, 'src="matrix/scene-1-thin-100-light.png"').Count) 1 'matrix photo appears once'
    $cases = @(
        @{ name = 'missing-index'; captured = 0; mutate = { Remove-Item $index } },
        @{ name = 'missing-findings'; captured = 0; mutate = { Remove-Item $findingPath } },
        @{ name = 'missing-cell'; captured = 15; mutate = {
            @(Import-Csv $index | Select-Object -Skip 1) | Export-Csv $index -NoTypeInformation
        } },
        @{ name = 'missing-file'; captured = 15; mutate = { Remove-Item (Join-Path $caseOutput 'matrix/scene-1-thin-100-light.png') } },
        @{ name = 'duplicate-cell'; captured = 15; mutate = {
            $rows = @(Import-Csv $index); ($rows + $rows[0]) | Export-Csv $index -NoTypeInformation
        } },
        @{ name = 'foreign-finding'; captured = 16; mutate = {
            [IO.File]::WriteAllText($findingPath, '[{"file":"alien.png","scene":"alien","size":"thin","zoom":100,"theme":"light","expected":false}]')
        } },
        @{ name = 'finding-count'; captured = 15; mutate = {
            $rows = @(Import-Csv $index); $rows[0].findings = '1'; $rows | Export-Csv $index -NoTypeInformation
        } },
        @{ name = 'malformed-json'; captured = 0; mutate = { [IO.File]::WriteAllText($findingPath, '[') } },
        @{ name = 'wrong-dimensions'; captured = 15; mutate = {
            $rows = @(Import-Csv $index); $rows[0].width = '2'; $rows | Export-Csv $index -NoTypeInformation
        } },
        @{ name = 'foreign-scene'; captured = 15; mutate = {
            $rows = @(Import-Csv $index); $rows[0].scene = 'alien'; $rows | Export-Csv $index -NoTypeInformation
        } },
        @{ name = 'wrong-png-size'; captured = 15; mutate = {
            $path = Join-Path $caseOutput 'matrix/scene-1-thin-100-light.png'
            $bytes = [IO.File]::ReadAllBytes($path); $bytes[19] = 2; [IO.File]::WriteAllBytes($path, $bytes)
        } },
        @{ name = 'truncated-png'; captured = 15; mutate = {
            $path = Join-Path $caseOutput 'matrix/scene-1-thin-100-light.png'
            $bytes = [IO.File]::ReadAllBytes($path); [IO.File]::WriteAllBytes($path, $bytes[0..45])
        } }
    )
    foreach ($case in $cases) {
        $caseOutput = Join-Path $fixtureRoot $case.name
        $casePlan = New-AllPlan @('scene-1')
        $batch = [pscustomobject]@{ Number = 1; Scenes = @('scene-1') }
        Write-AllFixture $caseOutput $casePlan $batch.Scenes | Out-Null
        Save-MotifCaptureBatch $caseOutput $batch 0
        $index = Join-Path $caseOutput 'matrix/batches/batch-001-index.csv'
        $findingPath = Join-Path $caseOutput 'matrix/batches/batch-001-findings.json'
        & $case.mutate
        $result = Merge-MotifCaptureBatches $caseOutput $casePlan @($batch)
        Assert-AllEqual $result.complete $false "$($case.name) is incomplete"
        Assert-AllEqual $result.success $false "$($case.name) fails the command"
        Assert-AllEqual $result.captured.Count $case.captured "$($case.name) retains only valid cells"
        Assert-AllEqual ($result.issues.Count -gt 0 -or $result.absent.Count -gt 0) $true "$($case.name) explains partial output"
        if ($case.name -eq 'missing-cell') {
            New-MotifCaptureSheet -OutputDirectory $caseOutput | Out-Null
            $html = [IO.File]::ReadAllText((Join-Path $caseOutput 'index.html'))
            Assert-AllEqual $html.Contains('Matrix capture incomplete') $true 'partial sheet is labelled'
            Assert-AllEqual $html.Contains('15 of 16') $true 'partial sheet counts are visible'
            Assert-AllEqual $html.Contains('capture-completeness.json') $true 'partial sheet links full inventory'
        }
    }
    $partialCalls = [Collections.Generic.List[string]]::new()
    $partialRunner = {
        param($Filter, $Log)
        if ($Filter -like '*WriteCaptureMatrixPlan' -or $Filter -notlike '*CaptureScenesAtEveryMatrixSizeAndWriteAllFindings') {
            return & $runner $Filter $Log
        }
        $partialCalls.Add($env:MOTIF_CAPTURE_SCENES)
        if ($partialCalls.Count -eq 1) {
            $partialPlan = New-AllPlan @('scene-1')
            $partialPlan.points = @($partialPlan.points[0])
            Write-AllFixture $env:MOTIF_SCREENSHOTS $partialPlan @('scene-1') -Finding | Out-Null
            return 7
        }
        if ($partialCalls.Count -eq 2) { return 9 }
        Write-AllFixture $env:MOTIF_SCREENSHOTS $plan ($env:MOTIF_CAPTURE_SCENES -split ',') | Out-Null
        return 0
    }
    $partialOutput = Join-Path $fixtureRoot 'partial-processes'
    $saved = Set-MotifCaptureEnvironment @{ MOTIF_CAPTURE_MATRIX = 'all'; MOTIF_CAPTURE_SCENES = 'inherited' }
    try {
        $result = Invoke-MotifAllCaptures $partialOutput $partialRunner
        Assert-AllEqual $env:MOTIF_CAPTURE_MATRIX 'all' 'caller all mode restored'
        Assert-AllEqual $env:MOTIF_CAPTURE_SCENES 'inherited' 'caller scenes restored'
    }
    finally { Restore-MotifCaptureEnvironment $saved }
    Assert-AllEqual $partialCalls.Count 3 'later batches run after partial and missing artifacts'
    Assert-AllEqual $result.complete $false 'failed partial run is incomplete'
    Assert-AllEqual $result.success $false 'failed partial run is nonzero'
    Assert-AllEqual $result.captured.Count 113 'one checkpoint plus final 112 cells retained'
    Assert-AllEqual $result.absent.Count 223 'missing cells are explicitly counted'
    Assert-AllEqual ($result.batches.ExitCode -join ',') '7,9,0' 'all batch exits retained'
    Assert-AllEqual $result.unexpectedFindings 1 'partial checkpoint finding retained'
    Assert-AllEqual (Test-Path (Join-Path $partialOutput 'matrix/batches/batch-002-index.csv')) $false 'missing batch cannot inherit previous index'
    Assert-AllEqual (Test-Path (Join-Path $partialOutput 'matrix/batches/batch-002-findings.json')) $false 'missing batch cannot inherit previous findings'
    Assert-AllEqual (Test-Path (Join-Path $partialOutput 'matrix/batches/batch-001-findings.json')) $true 'independent checkpoint snapshot retained'
    $staleOutput = Join-Path $fixtureRoot 'stale-catalog'
    [IO.Directory]::CreateDirectory((Join-Path $staleOutput 'matrix')) | Out-Null
    $plan | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $staleOutput 'matrix/capture-plan.json')
    $result = Invoke-MotifAllCaptures $staleOutput { param($Filter, $Log) return 0 }
    Assert-AllEqual $result.complete $false 'skipped catalog cannot reuse a previous plan'
    Assert-AllEqual $result.batches.Count 0 'unavailable catalog invents no batches'
    Assert-AllEqual (Test-Path (Join-Path $staleOutput 'matrix/capture-completeness.json')) $true 'catalog failure still writes completeness'
    Write-Host "Capture-All regression checks passed: $passed assertions."
}
finally { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue }
