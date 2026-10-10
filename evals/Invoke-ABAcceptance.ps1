[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Screen', 'Full', 'Confirmation', 'Evaluate', 'BigTierRelease')][string] $Mode,
    [string] $Question,
    [string[]] $TaskIds = @(),
    [string] $ScreenSummary,
    [string] $FullSummary,
    [string] $ConfirmationSummary,
    [string] $BaselineSummary,
    [string] $Evidence,
    [string] $CandidateArm,
    [string] $BaselineArm,
    [string] $FullComparisonQuestion,
    [string] $OutputDirectory,
    [string] $Configuration = 'Debug',
    [switch] $DryRun,
    [switch] $Keep
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'tools/ABHarness.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'tools/ABAcceptance.psm1') -Force
$outputRoot = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) }
    else { Join-Path $PSScriptRoot ('results/acceptance-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss')) }

function Get-ABAcceptanceTasks([bool] $ConfirmationSet = $false) {
    $root = Get-ABSetRoot $repoRoot -Confirmation:$ConfirmationSet
    $tasks = @(Get-ChildItem -LiteralPath $root -Filter 'task.yaml' -File -Recurse | ForEach-Object {
        $task = Read-ABJson $_.FullName
        if (Test-ABTaskFuture $task) { return }
        [string]$task.id
    } | Sort-Object -Unique)
    return @{ root = $root; ids = $tasks }
}

function Invoke-ABAcceptanceQuestion([string] $QuestionPath, [string[]] $SelectedTasks, [int] $TrialCount,
    [bool] $UseConfirmation, [bool] $PlanOnly) {
    $absoluteQuestion = if ([IO.Path]::IsPathRooted($QuestionPath)) { $QuestionPath } else { Join-Path $repoRoot $QuestionPath }
    $questionData = Read-ABJson $absoluteQuestion
    $questionData.tasks = @($SelectedTasks)
    $questionData.trials = $TrialCount
    $temporaryQuestion = Join-Path ([IO.Path]::GetTempPath()) ('motif-acceptance-' + [Guid]::NewGuid().ToString('N') + '.json')
    Write-ABJson $temporaryQuestion $questionData
    try {
        $arguments = @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Invoke-ABQuestion.ps1'),
            '-Question', $temporaryQuestion, '-Configuration', $Configuration, '-Parallel', '2')
        if ($UseConfirmation) { $arguments += '-Confirmation' }
        if ($PlanOnly) { $arguments += '-DryRun' }
        if ($Keep) { $arguments += '-Keep' }
        $run = Invoke-ABProcess (Get-Command pwsh).Source $arguments $repoRoot @{} 28800000
        if ($run.ExitCode -ne 0) { throw "Acceptance run failed:`n$($run.Stdout)`n$($run.Stderr)" }
        if ($PlanOnly) { Write-Host $run.Stdout; return $null }
        $match = [regex]::Match($run.Stdout, '(?m)^Summary: (?<path>.+)$')
        if (-not $match.Success) { throw "Acceptance run did not report its summary path:`n$($run.Stdout)" }
        return $match.Groups['path'].Value.Trim()
    }
    finally { Remove-Item -LiteralPath $temporaryQuestion -Force -ErrorAction SilentlyContinue }
}

if ($Mode -in @('Screen', 'Full', 'Confirmation', 'BigTierRelease')) {
    if ($Mode -eq 'BigTierRelease' -and -not $BaselineSummary) {
        throw 'Big-tier release checks require -BaselineSummary to evaluate the regression trigger.'
    }
    $useConfirmation = $Mode -eq 'Confirmation'
    $questionPath = if ($Mode -eq 'BigTierRelease') { 'evals/questions/big-tier-release.yaml' }
        elseif ($Question) { $Question }
        else { throw "$Mode requires -Question to name the baseline and candidate Arms." }
    if ($Mode -eq 'Full' -or $Mode -eq 'BigTierRelease') {
        $available = Get-ABAcceptanceTasks
        if ($available.ids.Count -ne 21) { throw "The full regression set must contain 21 active tasks; found $($available.ids.Count)." }
        $taskIds = @($available.ids)
    } elseif ($Mode -eq 'Screen') {
        $taskIds = @($TaskIds | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Sort-Object -Unique)
        if ($taskIds.Count -lt 4 -or $taskIds.Count -gt 6) { throw 'A screen needs four to six distinct affected task IDs.' }
    } else {
        if (-not $env:MOTIF_CONFIRMATION_GRAMMARS) { throw 'Fresh confirmation requires MOTIF_CONFIRMATION_GRAMMARS.' }
        $available = Get-ABAcceptanceTasks -ConfirmationSet
        $taskIds = @($TaskIds | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Sort-Object -Unique)
        if ($taskIds.Count -ne 5) { throw 'Fresh confirmation uses five distinct task IDs, one Episode per task and Arm.' }
        $missing = @($taskIds | Where-Object { $_ -notin $available.ids })
        if ($missing.Count) { throw "Confirmation tasks are absent from the pinned confirmation set: $($missing -join ', ')" }
    }
    $trialCount = if ($Mode -eq 'Screen') { 3 } elseif ($Mode -eq 'Full') { 3 } else { 1 }
    $summaryPath = Invoke-ABAcceptanceQuestion $questionPath $taskIds $trialCount $useConfirmation ([bool]$DryRun)
    if ($DryRun) { return }
    Write-Host "Acceptance run summary: $summaryPath"
    if ($Mode -eq 'BigTierRelease' -and $BaselineSummary) {
        $current = Read-ABJson $summaryPath
        $baseline = Read-ABJson $BaselineSummary
        $trigger = Get-ABBigTierRegression $current $baseline
        $trigger.currentSummary = $summaryPath
        $trigger.baselineSummary = [IO.Path]::GetFullPath($BaselineSummary)
        $triggerPath = Join-Path (Split-Path -Parent $summaryPath) 'big-tier-release.json'
        Write-ABJson $triggerPath $trigger
        Write-Host "Big-tier release report: $triggerPath"
        Write-Host "Verdict: $($trigger.verdict)"
        Write-Host "Decision: $($trigger.decision)"
        Write-Host $trigger.action
        foreach ($review in $trigger.personReview) {
            Write-Host "Person review: $($review.task) / $($review.arm): $($review.reasons -join '; ')"
        }
        if (-not $trigger.valid) { exit 2 }
    }
    return
}

foreach ($required in @($ScreenSummary, $FullSummary, $ConfirmationSummary, $BaselineSummary, $Evidence, $CandidateArm, $BaselineArm)) {
    if (-not $required) { throw 'Evaluate requires screen, full, confirmation and baseline summaries, evidence, and both Arm IDs.' }
}
$screen = Read-ABJson $ScreenSummary
$full = Read-ABJson $FullSummary
$confirmation = Read-ABJson $ConfirmationSummary
$baseline = Read-ABJson $BaselineSummary
$evidenceData = Read-ABJson $Evidence
$result = Get-ABAcceptanceResult $screen $full $confirmation $baseline $evidenceData $CandidateArm $BaselineArm
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$reportPath = Join-Path $outputRoot 'acceptance.json'
Write-ABJson $reportPath $result
$markdown = [Collections.Generic.List[string]]::new()
$markdown.Add("# Acceptance: $($result.decision)")
$markdown.Add('')
$markdown.Add("Candidate Arm: $CandidateArm | Baseline Arm: $BaselineArm")
$markdown.Add('')
$markdown.Add('| Gate | Result | Evidence |')
$markdown.Add('|---|---|---|')
foreach ($gate in $result.gates) {
    $markdown.Add("| $($gate.id) | $(if ($gate.passed) { 'pass' } else { 'hold' }) | $($gate.detail) |")
}
$markdown.Add('')
$markdown.Add('## What would settle the Hold')
$markdown.Add('')
if ($result.decision -eq 'Ship') { $markdown.Add('All five gates pass.') }
else { foreach ($item in $result.whatWouldSettleHold) { $markdown.Add('- ' + $item) } }
Set-Content -LiteralPath (Join-Path $outputRoot 'acceptance.md') -Value ($markdown -join "`n") -Encoding utf8
Write-Host "Acceptance report: $reportPath"
Write-Host "Decision: $($result.decision)"
if ($result.decision -eq 'Hold') { exit 2 }
