[CmdletBinding()]
param([Parameter(Mandatory)][string] $Config)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ABHarness.psm1') -Force
$configData = Read-ABJson $Config
$integrity = Read-ABJson $configData.integrityPath
if ($integrity.state -ne 'clean' -or [double]$integrity.closedUtc -ge [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() / 1000.0) {
    throw 'Grading requires a closed, clean trial.'
}
foreach ($name in $integrity.bundleHashes.Keys) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $configData.bundle $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $integrity.bundleHashes[$name]) { throw 'The frozen output bundle changed after closure.' }
}
$configData.task.gradingPrompt = Get-Content -LiteralPath (Join-Path $configData.task.taskPath $configData.task.prompt) -Raw
$gradingSetPath = [string]$configData.setPath
if ($configData.confirmation) {
    $confirmationRoot = Get-ABSetRoot $configData.repoRoot -Confirmation
    $gradingSetPath = Join-Path $confirmationRoot ([string]$configData.task.set)
    $privateTaskPath = Join-Path $gradingSetPath ("tasks/" + [string]$configData.task.id)
    if (Test-Path -LiteralPath $privateTaskPath -PathType Container) { $configData.task.taskPath = $privateTaskPath }
}
$hostData = Read-ABJson (Join-Path $configData.bundle 'host.json')
$hostData.primaryMetric = $configData.primaryMetric
$started = [DateTimeOffset]::UtcNow
if ($hostData.Contains('failureClass') -and $hostData.failureClass -eq 'agent_failure') {
    $intent = Read-ABJson (Join-Path $configData.bundle 'intent.json')
    $operations = if ($intent -and $intent.Contains('operations')) { @($intent.operations) } else { @() }
    $grade = [ordered]@{
        grade = 0.0; success = $false; primaryMetric = $configData.primaryMetric; primaryScore = 0.0
        operationCount = $operations.Count; gradingState = 'measured'; failureClass = 'agent_failure'
        failureEvidence = @([string]$(if ($hostData.Contains('failure')) { $hostData.failure } else { 'Agent execution failed within its budget.' }))
        graders = @(); agentBudget = $hostData.agentBudget; agentElapsedMs = $hostData.agentElapsedMs
        gradedUtc = $started.ToString('O'); trialClosedUtc = $integrity.closedUtc
    }
    Write-ABJson (Join-Path $configData.outputRoot 'proposals.json') @{ proposal = $intent; operationCount = $operations.Count }
    Write-ABJson (Join-Path $configData.outputRoot 'grade.json') $grade
    return
}
$measurementEnvironment = @{
    MOTIF_WORKER_ROOT = Join-Path $configData.scratchRoot 'work'
    MOTIF_RUNNER_NAMESPACE = 'grade-' + [Guid]::NewGuid().ToString('N')
    MOTIF_WRITING_SYSTEM_REPOSITORY_PATH = Join-Path $configData.scratchRoot 'writing-systems'
    MOTIF_TEST_SLDR_CACHE_PATH = Join-Path $configData.scratchRoot 'sldr'
    MOTIF_TEST_SLDR_OFFLINE = '1'
}
$measurement = Invoke-ABProcess $configData.builder @('measure-output', '--set', $configData.setPath,
    '--start', $configData.task.start, '--words-root', $gradingSetPath, '--proposal', (Join-Path $configData.bundle 'intent.json'),
    '--out', $configData.scratchRoot) $configData.outputRoot $measurementEnvironment 720000
if ($measurement.ExitCode) { throw "Independent output measurement failed: $($measurement.Stderr)" }
$measured = $measurement.Stdout | ConvertFrom-Json -AsHashtable
$activity = @(Read-ABLines (Join-Path $configData.bundle 'activity.jsonl'))
$operations = @($measured.proposal.operations).Count
$judgeFamilies = if ($configData.Contains('judgeFamilies')) { @($configData.judgeFamilies) } else { @('opus', 'sol') }
$grade = Get-ABGrade $configData.task $gradingSetPath @($measured.parserRows) $operations $hostData $activity `
    (Join-Path $configData.bundle 'transcript.jsonl') $configData.profile $measured.proposal $integrity `
    $measured.lexiconBefore $measured.lexiconAfter $measured.proposalFailure $judgeFamilies
$grade.gradedUtc = $started.ToString('O')
$grade.trialClosedUtc = $integrity.closedUtc
Write-ABJson (Join-Path $configData.outputRoot 'proposals.json') @{ proposal = $measured.proposal; operationCount = $operations }
Write-ABJson (Join-Path $configData.outputRoot 'grade.json') $grade
