[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $RunDirectory,
    [string] $Configuration = 'Debug',
    [switch] $Confirmation,
    [string] $Tasks = '*'
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
$setRoot = Get-ABSetRoot $repoRoot
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
        if ($manifest.integrity.state -eq 'clean') {
            $arm = Read-ABJson (Join-Path $PSScriptRoot "arms/$($stored.arm).yaml")
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
                profile = $profile; outputRoot = $outputRoot; scratchRoot = Join-Path $scratchRoot ([Guid]::NewGuid().ToString('N'))
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
            elseif ($grade.Contains('failureClass') -and $grade.failureClass -eq 'harness_defect') { 'harness_defect' }
            elseif ($grade.Contains('failureClass') -and $grade.failureClass -eq 'cloud_failure') { 'cloud_failure' }
            elseif ($grade.Contains('gradingState') -and $grade.gradingState -eq 'judge_disagreement') { 'judge_disagreement' }
            elseif ($null -eq $grade.success) { 'inconclusive' } elseif ($grade.success) { 'passed' } else { 'failed' }
        $row.trialRoot = $outputRoot; $row.sourceTrialRoot = $trialSource
        $row.activity = @(Read-ABLines (Join-Path $trialSource 'frozen/activity.jsonl'))
        $completed.Add($row)
        Write-Host "Re-graded $($stored.task) / $($stored.arm) / $($stored.trial): $($row.status)."
    }
    $trialCount = [int](($source.arms | Select-Object -First 1).requestedTrials)
    Write-ABReport $completed $selectedTasks $source.arms $trialCount $source.primaryMetric @{
        question = $source.question
    } $source.questionId $runId $runRoot $source.measurementFingerprint
    $summaryPath = Join-Path $runRoot 'summary.json'
    $summary = Read-ABJson $summaryPath
    $summary.sourceRun = $sourceRoot
    $summary.sourceMeasurementFingerprint = $source.measurementFingerprint
    $summary.integrityReused = $true
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
