[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $RunDirectory,
    [string] $Configuration = 'Debug',
    [switch] $Confirmation,
    [string] $Tasks = '*',
    [ValidateSet('paired', 'other-family-alone')][string] $JudgePolicy
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'tools/ABHarness.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'tools/ABReport.psm1') -Force
$sourceRoot = (Resolve-Path -LiteralPath $RunDirectory).ProviderPath
$source = Read-ABJson (Join-Path $sourceRoot 'summary.json')
if (-not $source.Contains('measurementFingerprint') -or [string]$source.measurementFingerprint -notmatch '^[0-9a-f]{64}$') {
    throw 'This stored run has no valid measurement fingerprint. Recreate it with Invoke-ABQuestion before regrading.'
}
if (-not $JudgePolicy -and $source.Contains('judgePolicy')) { $JudgePolicy = [string]$source.judgePolicy }
$questionPath = Join-Path $PSScriptRoot ("questions/{0}.yaml" -f $source.questionId)
if (-not $JudgePolicy -and (Test-Path -LiteralPath $questionPath -PathType Leaf)) {
    $storedQuestion = Read-ABJson $questionPath
    if ($storedQuestion.Contains('judge_policy')) { $JudgePolicy = [string]$storedQuestion.judge_policy }
}
if (-not $JudgePolicy) { $JudgePolicy = 'paired' }
$storedConfirmation = $source.Contains('confirmation') -and [bool]$source.confirmation
if (($Confirmation -and -not $source.Contains('confirmation')) -or $storedConfirmation -ne [bool]$Confirmation) {
    throw 'The regrade confirmation setting must match the stored run, which must record it.'
}
$setRoot = Get-ABSetRoot $repoRoot -Confirmation:$Confirmation
if ($Confirmation) { [void](Set-ABConfirmationTrialRoot $repoRoot (Get-ABConfirmationResultsRoot $repoRoot)) }
$runId = 'regrade-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $sourceRoot $runId
New-Item -ItemType Directory -Path $runRoot | Out-Null
$builder = Join-Path $repoRoot ("bin/{0}/SIL.Motif.EvalSets{1}" -f $Configuration, $(if ($IsWindows) { '.exe' } else { '' }))
$completed = [Collections.Generic.List[object]]::new()
$selectedTasks = [Collections.Generic.List[object]]::new()
foreach ($taskRow in $source.tasks | Where-Object { $_.id -like $Tasks }) {
    $taskPath = Join-Path $setRoot ("$($taskRow.set)/tasks/$($taskRow.id)")
    $task = Read-ABJson (Join-Path $taskPath 'task.yaml')
    $task.taskPath = $taskPath
    $task.setPath = Join-Path $setRoot ([string]$task.set)
    $selectedTasks.Add($task)
}
if (-not $selectedTasks.Count) { throw 'The task selector matched no stored tasks.' }
$scratchRoot = New-ABTrialDirectory $repoRoot
try {
    foreach ($stored in $source.trials | Where-Object { $_.task -in @($selectedTasks | ForEach-Object { $_.id }) }) {
        $task = @($selectedTasks | Where-Object { $_.id -eq $stored.task })[0]
        $relative = "trials/$($stored.task)/$($stored.arm)/trial-$($stored.trial)"
        $trialSource = Join-Path $sourceRoot $relative
        $outputRoot = Join-Path $runRoot $relative
        New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
        $manifest = Read-ABJson (Join-Path $trialSource 'manifest.json')
        $grade = Read-ABJson (Join-Path $trialSource 'grade.json')
        # The live run never grades an Episode its boundary excluded, so neither does a re-grade.
        $boundaryClass = if ($manifest.integrity.Contains('failureClass')) { [string]$manifest.integrity.failureClass } else { '' }
        if ($manifest.integrity.state -eq 'clean' -and $boundaryClass -notin @('cloud_failure', 'harness_defect')) {
            $sourceArmId = [string]$stored.arm -replace '\.a[12]$', ''
            $arm = Read-ABJson (Join-Path $PSScriptRoot "arms/$sourceArmId.yaml")
            $profile = Read-ABJson (Join-Path $PSScriptRoot ([string]$arm.server.grade_profile))
            # Grade against the tools this trial's server offered, as the live trial did, not today's profile file.
            $served = Join-Path $trialSource 'server.client-tools.json'
            if (-not (Test-Path -LiteralPath $served)) { throw "Stored trial has no served tool list: $served" }
            $classes = @{}
            foreach ($tool in $profile.tools) { $classes[$tool.name] = $tool.class }
            $profile.tools = @(Read-ABJson $served | ForEach-Object { [string]$_.name } | ForEach-Object {
                @{ name = $_; class = if ($classes.Contains($_)) { $classes[$_] } else { 'Read' } } })
            $config = @{
                repoRoot = $repoRoot; confirmation = [bool]$Confirmation
                bundle = Join-Path $trialSource 'frozen'; integrityPath = Join-Path $trialSource 'integrity.json'
                task = $task; setPath = $task.setPath; builder = $builder; primaryMetric = $source.primaryMetric
                profile = $profile; judgeFamilies = @(Get-ABJudgeFamilies $arm $JudgePolicy); outputRoot = $outputRoot; scratchRoot = Join-Path $scratchRoot ([Guid]::NewGuid().ToString('N'))
            }
            $configPath = Join-Path $scratchRoot 'grading.json'
            Write-ABJson $configPath $config
            $run = Invoke-ABProcess (Get-Command pwsh).Source @('-NoProfile', '-File',
                (Join-Path $PSScriptRoot 'tools/Invoke-ABGrade.ps1'), '-Config', $configPath) $scratchRoot @{} 1600000
            if ($run.ExitCode) {
                $grade = @{ grade = $null; success = $null; primaryScore = $null; operationCount = 0
                    primaryMetric = $source.primaryMetric; graders = @(); gradingState = 'harness_defect';
                    failureClass = 'harness_defect'; failureEvidence = @($run.Stderr) }
                Write-ABJson (Join-Path $outputRoot 'grade.json') $grade
            } else { $grade = Read-ABJson (Join-Path $outputRoot 'grade.json') }
        } else { Write-ABJson (Join-Path $outputRoot 'grade.json') $grade }
        $row = @{} + $stored
        $row.Remove('files')
        $row.integrity = $manifest.integrity
        $row.primaryMetric = if ($grade.Contains('primaryMetric')) { $grade.primaryMetric } else { $source.primaryMetric }
        $row.grade = $grade.grade; $row.primaryScore = $grade.primaryScore; $row.success = $grade.success
        $row.failureClass = if ($grade.Contains('failureClass') -and $grade.failureClass) { $grade.failureClass }
            elseif ($manifest.Contains('failureClass')) { $manifest.failureClass } else { $null }
        $row.failureEvidence = $(if ($grade.Contains('failureEvidence')) { @($grade.failureEvidence) } else { @() }) +
            $(if ($manifest.Contains('failureEvidence')) { @($manifest.failureEvidence) } else { @() })
        $row.gradingState = if ($grade.Contains('gradingState')) { $grade.gradingState } else { 'unscored' }
        $row.judgeFamilies = @($grade.graders | Where-Object { $_.Contains('judge') -and $_.judge -and $_.judge.Contains('judges') } |
            ForEach-Object { $_.judge.judges | ForEach-Object { [string]$_.family } } | Sort-Object -Unique)
        $judgeVerdicts = @($grade.graders | Where-Object { $_.Contains('judge') -and $_.judge -and $_.judge.Contains('verdict') } |
            ForEach-Object { [string]$_.judge.verdict } | Sort-Object -Unique)
        $row.judgeVerdict = if ($judgeVerdicts.Count -eq 0) { 'not judged' }
            elseif ($judgeVerdicts.Count -eq 1) { $judgeVerdicts[0] } else { 'mixed' }
        $row.judgeDisagreements = @($grade.graders | Where-Object { $_.Contains('judge') -and $_.judge -and $_.judge.disagreement }).Count
        $row.operationCount = $grade.operationCount
        $row.status = if ($manifest.integrity.state -ne 'clean') { $manifest.status }
            elseif ($row.failureClass -in @('harness_defect', 'cloud_failure')) { [string]$row.failureClass }
            elseif ($grade.Contains('gradingState') -and $grade.gradingState -eq 'judge_disagreement') { 'judge_disagreement' }
            elseif ($null -eq $grade.success) { 'inconclusive' } elseif ($grade.success) { 'passed' } else { 'failed' }
        $row.trialRoot = $outputRoot; $row.sourceTrialRoot = $trialSource
        $row.activity = @(Read-ABLines (Join-Path $trialSource 'frozen/activity.jsonl'))
        $completed.Add($row)
        Write-Host "Re-graded $($stored.task) / $($stored.arm) / $($stored.trial): $($row.status)."
    }
    $trialCount = [int](($source.arms | Select-Object -First 1).requestedTrials)
    $reportQuestion = [ordered]@{
        question = $source.question
        judge_policy = $JudgePolicy
        mode = if ($source.Contains('mode')) { [string]$source.mode } else { 'ab' }
        aaArm = $null
    }
    if ($reportQuestion.mode -eq 'aa' -and $source.Contains('aa') -and $source.aa) {
        $reportQuestion.aaArm = [string]$source.aa.arm
    }
    Write-ABReport $completed $selectedTasks $source.arms $trialCount $source.primaryMetric @{
        question = $reportQuestion.question; judge_policy = $reportQuestion.judge_policy
        mode = $reportQuestion.mode; aaArm = $reportQuestion.aaArm
    } $source.questionId $runId $runRoot $source.measurementFingerprint
    $summaryPath = Join-Path $runRoot 'summary.json'
    $summary = Read-ABJson $summaryPath
    $summary.sourceRun = $sourceRoot
    $summary.sourceMeasurementFingerprint = $source.measurementFingerprint
    $summary.integrityReused = $true
    $summary.confirmation = [bool]$Confirmation
    if ($source.Contains('confirmationCommit')) { $summary.confirmationCommit = $source.confirmationCommit }
    # The harness accepts only the pinned grammars, so an older run is graded with newer keys.
    $summary.sourceTestGrammars = if ($source.Contains('testGrammars')) { $source.testGrammars } else { $null }
    $currentPin = $summary.testGrammars | ConvertTo-Json -Compress
    $summary.testGrammarsChanged = ($summary.sourceTestGrammars | ConvertTo-Json -Compress) -ne $currentPin
    if ($summary.testGrammarsChanged) {
        $recorded = if ($summary.sourceTestGrammars) { "$($summary.sourceTestGrammars.tag) ($($summary.sourceTestGrammars.commit))" } else { 'no recorded revision' }
        $current = if ($summary.testGrammars) { "$($summary.testGrammars.tag) ($($summary.testGrammars.commit))" } else { 'no pinned revision' }
        Add-Content -LiteralPath (Join-Path $runRoot 'report.md') -Value ("`nRe-graded with test grammars $current; the original run recorded $recorded, so task keys may differ from the ones its agents faced.")
    }
    for ($index = 0; $index -lt $completed.Count; $index++) {
        $row = $completed[$index]
        foreach ($name in @('manifest', 'transcript', 'activity')) {
            $summary.trials[$index].files[$name] = Join-Path $row.sourceTrialRoot "$name$(if ($name -in @('activity', 'transcript')) { '.jsonl' } else { '.json' })"
        }
        if (-not (Test-Path -LiteralPath $summary.trials[$index].files.proposals)) {
            $summary.trials[$index].files.proposals = Join-Path $row.sourceTrialRoot 'proposals.json'
        }
    }
    Write-ABJson $summaryPath $summary
}
finally { Remove-ABTrialDirectory $scratchRoot }
