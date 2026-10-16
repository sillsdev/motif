[CmdletBinding()]
param(
    [switch] $ValidityOnly,
    [string] $TaskFilter,
    [string] $ArmManifest,
    [string] $Configuration = 'Debug',
    [switch] $Confirmation,
    [Alias('-keep')][switch] $Keep
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $PSScriptRoot 'tools'
Import-Module (Join-Path $toolsRoot 'ABHarness.psm1') -Force
$setRoot = Get-ABSetRoot $repoRoot -Confirmation:$Confirmation
$resultsRoot = if ($Confirmation) { Get-ABConfirmationResultsRoot $repoRoot }
    else { Join-Path $PSScriptRoot 'results' }
if ($Confirmation) { [void](Set-ABConfirmationTrialRoot $repoRoot $resultsRoot) }

$parserPath = $env:MOTIF_PANGLOSS_EXE
if ([string]::IsNullOrWhiteSpace($parserPath) -or -not (Test-Path -LiteralPath $parserPath -PathType Leaf)) {
    throw 'Validity checks require MOTIF_PANGLOSS_EXE to point to the pinned PanGloss executable.'
}
$release = Read-ABJson (Join-Path $repoRoot 'pangloss-release.json')
$architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$rid = if ($IsWindows) { "win-$architecture" } elseif ($IsMacOS) { "osx-$architecture" } else { "linux-$architecture" }
if (-not $release.assets.Contains($rid)) { throw "The pinned parser release has no asset for '$rid'." }
$expectedHash = [string]$release.assets[$rid].sha256
$parserStream = [IO.File]::OpenRead($parserPath)
try { $actualHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($parserStream)).ToLowerInvariant() }
finally { $parserStream.Dispose() }
if ($actualHash -ne $expectedHash) {
    throw "MOTIF_PANGLOSS_EXE is not the pinned $($release.version) $rid asset. Expected SHA-256 $expectedHash; found $actualHash."
}
$motifExe = Join-Path $repoRoot ("bin/{0}/motif{1}" -f $Configuration, $(if ($IsWindows) { '.exe' } else { '' }))
$builderExe = Join-Path $repoRoot ("bin/{0}/SIL.Motif.EvalSets{1}" -f $Configuration, $(if ($IsWindows) { '.exe' } else { '' }))
if (-not (Test-Path -LiteralPath $motifExe) -or -not (Test-Path -LiteralPath $builderExe)) {
    throw "Build the solution with pwsh ./build.ps1 -Configuration $Configuration before running validity checks."
}

$tasks = @(Get-ChildItem -LiteralPath $setRoot -Filter 'task.yaml' -File -Recurse | ForEach-Object {
    $task = Read-ABJson $_.FullName
    if (Test-ABTaskFuture $task) { return }
    if ($TaskFilter -and [string]$task.id -notlike $TaskFilter) { return }
    $task.taskPath = $_.Directory.FullName
    $task.setPath = Join-Path $setRoot ([string]$task.set)
    $task.promptPath = Join-Path $task.taskPath ([string]$task.prompt)
    $task.primaryMetric = if ($task.Contains('primary_metric')) { [string]$task.primary_metric } else { 'task-success' }
    $task
})
if ($tasks.Count -eq 0) {
    $variable = if ($Confirmation) { 'MOTIF_CONFIRMATION_GRAMMARS' } else { 'MOTIF_TEST_GRAMMARS' }
    throw "No tasks were found under $variable."
}
$confirmationScripts = @{}
if ($Confirmation) {
    foreach ($task in $tasks) { $confirmationScripts[$task.taskPath] = Get-ABConfirmationValidityScripts $task }
}
if ($TaskFilter -and $ValidityOnly) { throw '-ValidityOnly stamps every active task, so it cannot be combined with -TaskFilter.' }

$fakeArm = Read-ABJson (Join-Path $PSScriptRoot 'arms/fake-default.yaml')
$fakeArm.server.profilePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ([string]$fakeArm.server.grade_profile)))
$fakeArm.server.profileArgument = [string]$fakeArm.server.profile
$fakeArm.server.executable = [IO.Path]::GetFullPath($motifExe)
$fakeArm.host = 'fake'
[object[]]$fingerprintArms = @()
if ($ArmManifest) { $fingerprintArms = @(Read-ABJson $ArmManifest) }
if ($fingerprintArms.Count -eq 0) { $fingerprintArms = @($fakeArm) }
$validationArms = foreach ($sourceArm in $fingerprintArms) {
    $validationArm = @{} + $sourceArm
    $validationArm.server = @{} + $sourceArm.server
    $validationArm.host = 'fake'
    $validationArm.authMode = 'none'
    $validationArm
}
$stampPath = Join-Path $resultsRoot '.validity.json'
$sampleRoot = Join-Path $resultsRoot 'validity-samples'
$validityFinals = @(Read-ABJson (Join-Path $PSScriptRoot 'integrity/fixtures/validity-finals.json'))
$temporaryRoot = New-ABTrialDirectory $repoRoot
$originalJudgeCommand = $env:MOTIF_MEANING_JUDGE_COMMAND
$originalJudgeMode = $env:MOTIF_FAKE_JUDGE_MODE
$originalJudgeFinals = $env:MOTIF_FAKE_JUDGE_FINALS
if (-not $originalJudgeCommand) {
    $env:MOTIF_FAKE_JUDGE_MODE = 'validity'
    $env:MOTIF_MEANING_JUDGE_COMMAND = ConvertTo-Json -Compress -InputObject @(
        (Get-Command python3).Source, (Join-Path $PSScriptRoot 'integrity/fixtures/fake-meaning-judge.py')
    )
}

try {
    if ($Confirmation) {
        $confirmationFinals = [Collections.Generic.List[object]]::new()
        foreach ($task in $tasks) {
            foreach ($grader in $task.graders | Where-Object { $_.type -in @('answer', 'meaning', 'rubric') }) {
                $keyName = if ($grader.Contains('key')) { [string]$grader.key } else { 'answer.yaml' }
                $key = Read-ABJson (Join-Path $task.taskPath $keyName)
                if (-not $key.Contains('meaning')) { continue }
                $scripts = $confirmationScripts[$task.taskPath]
                $goldMessages = @(Read-ABLines $scripts.gold | Where-Object { $_.Contains('finalMessage') })
                $wrongMessages = @(Read-ABLines $scripts.wrong | Where-Object { $_.Contains('finalMessage') })
                if (-not $goldMessages.Count -or -not $wrongMessages.Count) {
                    throw "Confirmation task '$($task.id)' validity scripts must contain finalMessage records."
                }
                $confirmationFinals.Add([ordered]@{
                    required = @($key.meaning.required)
                    gold = [string]$goldMessages[-1].finalMessage
                    wrong = [string]$wrongMessages[-1].finalMessage
                })
                break
            }
        }
        $confirmationFinalsPath = Join-Path $temporaryRoot 'confirmation-validity-finals.json'
        Write-ABJson $confirmationFinalsPath @($confirmationFinals)
        $env:MOTIF_FAKE_JUDGE_FINALS = $confirmationFinalsPath
    }
    foreach ($arm in $validationArms) {
      foreach ($task in $tasks) {
        $cases = @(
            [ordered]@{ name = 'gold'; start = [string]$task.start; validationMode = $null; minimum = 0.99; maximum = 1.0 },
            [ordered]@{ name = 'empty'; start = [string]$task.start; validationMode = 'empty'; minimum = 0.0; maximum = 0.01 }
        )
        $wrongCase = if ($Confirmation) { $null } else { Get-ABValidityWrongCase $repoRoot $task $validityFinals }
        $cases += [ordered]@{ name = 'wrong'; start = [string]$task.start; validationMode = $null; minimum = 0.0; maximum = 0.01 }
        foreach ($case in $cases) {
            $safeTask = [string]$task.id
            $outputRoot = Join-Path $sampleRoot ("{0}/{1}/{2}" -f $safeTask, [string]$arm.id, $case.name)
            $privateRoot = New-ABTrialDirectory $repoRoot $temporaryRoot
            if (Test-Path -LiteralPath $outputRoot) { Remove-Item -LiteralPath $outputRoot -Recurse -Force }
            New-Item -ItemType Directory -Path $outputRoot, $privateRoot -Force | Out-Null
            $validationTask = Read-ABJson (Join-Path $task.taskPath 'task.yaml')
            $validationTask.start = [string]$case.start
            $manifest = [ordered]@{
                runId = 'validity'
                questionId = 'validity'
                trialRoot = $privateRoot
                outputRoot = $outputRoot
                trial = 1
                repoRoot = $repoRoot
                configuration = $Configuration
                primaryMetric = [string]$task.primaryMetric
                setPath = $task.setPath
                taskPath = $task.taskPath
                task = $validationTask
                arm = $arm
                validationMode = $case.validationMode
                confirmation = [bool]$Confirmation
                keep = [bool]$Keep
            }
            if ($Confirmation -and $case.name -in @('gold', 'wrong')) {
                $manifest.fakeScript = $confirmationScripts[$task.taskPath][$case.name]
            } elseif ($case.name -eq 'wrong' -and $wrongCase.Contains('script')) {
                $manifest.fakeScript = $wrongCase.script
            } elseif ($case.name -eq 'wrong') {
                $wrongScript = Join-Path $privateRoot 'wrong.jsonl'
                @{ finalMessage = $wrongCase.finalMessage } | ConvertTo-Json -Compress | Set-Content -LiteralPath $wrongScript -Encoding utf8
                $manifest.fakeScript = $wrongScript
            }
            $manifestPath = Join-Path $privateRoot 'run-manifest.json'
            Write-ABJson $manifestPath $manifest
            $run = Invoke-ABProcess (Get-Command pwsh).Source @(
                '-NoProfile', '-File', (Join-Path $toolsRoot 'Invoke-ABTrial.ps1'), '-RunManifest', $manifestPath
            ) $repoRoot @{} ([int]$task.limits.wall_seconds * 1000 + 300000)
            $resultPath = Join-Path $outputRoot 'grade.json'
            if ($run.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultPath)) {
                throw "The $($case.name) validity Trial failed for '$($task.id)': $($run.Stdout)`n$($run.Stderr)"
            }
            $trialResult = Read-ABJson (Join-Path $outputRoot 'manifest.json')
            if ($trialResult.integrity.state -ne 'clean') {
                $reasons = ($trialResult.integrity.reasons | ForEach-Object { $_.reason }) -join '; '
                throw "Validity trial '$($task.id)' was $($trialResult.integrity.state): $reasons"
            }
            $grade = Read-ABJson $resultPath
            $score = $grade.primaryScore
            if ($null -eq $score -or [double]$score -lt [double]$case.minimum -or [double]$score -gt [double]$case.maximum) {
                throw "Task '$($task.id)' $($case.name) primary score was '$score'; expected $($case.minimum)..$($case.maximum). See $resultPath"
            }
            if ($case.name -eq 'gold' -and $grade.success -ne $true) {
                throw "Task '$($task.id)' gold solution did not pass all graders. See $resultPath"
            }
            Write-Host "Validity $($arm.id) / $($task.id) / $($case.name): $score ($resultPath)"
        }
      }
    }

    $questionPath = Join-Path $PSScriptRoot 'questions/default-vs-lean-fake.yaml'
    $fakeReports = [Collections.Generic.List[string]]::new()
    $fakeTaskIds = if ($Confirmation) { @($tasks | ForEach-Object { [string]$_.id }) } else { @('') }
    foreach ($fakeTaskId in $fakeTaskIds) {
        $fakeArguments = @(
            '-NoProfile', '-File', (Join-Path $PSScriptRoot 'Invoke-ABQuestion.ps1'),
            '-Question', $questionPath, '-Trials', '1', '-Parallel', '2',
            '-Configuration', $Configuration, '-SkipValidityCheck'
        ) + $(if ($Confirmation) { @('-Confirmation', '-Tasks', $fakeTaskId) }
            elseif ($TaskFilter) { @('-Tasks', $TaskFilter) } else { @() }) +
            $(if ($Keep) { @('-Keep') } else { @() })
        $fakeRun = Invoke-ABProcess (Get-Command pwsh).Source $fakeArguments $repoRoot @{} 7200000
        if ($fakeRun.ExitCode -ne 0) { throw "The fake-host end-to-end question failed:`n$($fakeRun.Stdout)`n$($fakeRun.Stderr)" }
        $reportMatch = [regex]::Match($fakeRun.Stdout, '(?m)^Report: (?<path>.+)$')
        if (-not $reportMatch.Success) { throw "The fake-host run did not report a sample report path:`n$($fakeRun.Stdout)" }
        $sampleReport = $reportMatch.Groups['path'].Value.Trim()
        if (-not (Test-Path -LiteralPath $sampleReport -PathType Leaf)) { throw "The fake-host sample report is missing: $sampleReport" }
        $reportText = Get-Content -LiteralPath $sampleReport -Raw
        $summaryPath = Join-Path (Split-Path -Parent $sampleReport) 'summary.json'
        $sampleSummary = Read-ABJson $summaryPath
        if (@($sampleSummary.trials | Where-Object { $_.integrity.state -ne 'clean' -or $_.success -ne $true }).Count -gt 0) {
            throw "The fake-host A/B smoke trials did not all pass. See $summaryPath"
        }
        if ($Confirmation -and (@($sampleSummary.tasks).Count -ne 1 -or [string]$sampleSummary.tasks[0].id -ne $fakeTaskId)) {
            throw "The confirmation smoke selected tasks other than '$fakeTaskId'."
        }
        $firstLine = ($reportText -split "`r?`n")[0]
        # One filtered task is one independent unit, which has no interval, so its verdict is the no-interval sentence.
        if (-not $TaskFilter -and ([regex]::Matches($firstLine, '\.').Count -lt 2 -or $firstLine -notmatch 'not enough evidence|scored higher')) {
            throw "The sample report does not begin with the required two-sentence verdict: $firstLine"
        }
        if (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $sampleReport) 'summary.json') -PathType Leaf)) {
            throw "The fake-host run did not produce summary.json beside $sampleReport."
        }
        if ($reportText -notmatch '## Integrity' -or $sampleSummary.integrity.counts.clean -ne $sampleSummary.integrity.attempted) { throw 'The integrity report did not retain all clean trials.' }
        $fakeReports.Add($sampleReport)
        Write-Host "Fake-host end to end: $sampleReport"
    }
    $sampleReport = $fakeReports[0]

    if ($ValidityOnly) {
        $fingerprint = if ($ArmManifest) { Get-ABRepositoryFingerprint $repoRoot $Configuration $fingerprintArms }
            else { Get-ABRepositoryFingerprint $repoRoot $Configuration }
        $armFingerprints = [ordered]@{}
        if ($ArmManifest) {
            foreach ($arm in $fingerprintArms) {
                $armFingerprints[[string]$arm.id] = Get-ABRepositoryFingerprint $repoRoot $Configuration @($arm)
            }
        }
        Write-ABJson $stampPath ([ordered]@{
            fingerprint = $fingerprint
            armFingerprints = $armFingerprints
            configuration = $Configuration
            checkedUtc = [DateTimeOffset]::UtcNow.ToString('O')
            taskCount = $tasks.Count
            fakeReport = $sampleReport
            fakeReports = @($fakeReports)
        })
        Write-Host "Validity stamp: $stampPath"
    }
}
finally {
    $env:MOTIF_MEANING_JUDGE_COMMAND = $originalJudgeCommand
    $env:MOTIF_FAKE_JUDGE_MODE = $originalJudgeMode
    $env:MOTIF_FAKE_JUDGE_FINALS = $originalJudgeFinals
    Remove-ABTrialDirectory $temporaryRoot -Keep:$Keep
    if ($Keep) { Write-Host "Validity staging retained at: $temporaryRoot" }
}
