[CmdletBinding()]
param([Parameter(Mandatory)][string] $Session)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module /opt/client/host-functions.psm1 -Force
Import-Module /opt/client/host-plan.psm1 -Force
$manifest = Read-ABJson $Session
$privateRoot = '/session/state'
$agentDirectory = '/session/output'
$fwDataPath = '/session/project/project.fwdata'
$workerRoot = '/session/state/work'
$transcriptPath = '-'
$activityPath = '/session/output/activity.jsonl'
$hostResultPath = '/session/output/host.json'
$motifExe = '/opt/product/motif'
$task = $manifest.task
$serverCommand = $motifExe
$serverArguments = Get-ABServerArguments $manifest.profile $activityPath
$systemAppend = $manifest.systemAppend
$hostPlan = New-ABHostPlan $manifest.arm ([string]$task.promptText) $task.limits $systemAppend

$agentLaunchPath = Join-Path $privateRoot 'agent-launch.json'
foreach ($path in @('/session/agent/config', '/session/agent/state')) { New-Item -ItemType Directory $path -Force | Out-Null }
Write-ABJson '/session/agent/config/mcp.json' $hostPlan.mcpConfig
$agentBudget = Get-ABAgentBudget $task.limits
Write-ABJson $agentLaunchPath @{ plan = $hostPlan; host = $manifest.arm.host; serverCommand = $serverCommand
    serverArguments = $serverArguments; preflight = [bool]$manifest.preflight; toolCalls = $agentBudget.toolCalls }
foreach ($path in @($privateRoot, $agentDirectory, $workerRoot)) { New-Item -ItemType Directory $path -Force | Out-Null }
$registered = Invoke-ABProcess $motifExe (Get-ABProjectRegistrationArguments $fwDataPath) $agentDirectory @{} 300000
if ($registered.ExitCode -ne 0 -or $registered.Stderr -match 'could not be recorded') {
    throw "Known project registration failed: $($registered.Stderr)"
}
$baseline = Invoke-ABProcess $motifExe @('baseline', 'capture', '--project', $fwDataPath, '--json') $agentDirectory @{} 300000
if ($baseline.ExitCode -ne 0) { throw "Baseline capture failed: $($baseline.Stderr)" }
if ($manifest.preflight) {
    $checked = Invoke-ABProcess '/usr/bin/python3' @('/opt/client/agent-boundary.py', $agentLaunchPath) $agentDirectory @{} 60000
    if ($checked.ExitCode) { throw "Agent containment pre-flight failed: $($checked.Stderr)" }
    Import-Module /opt/client/protocol.psm1 -Force
    $sessionClient = New-ABMcpSession $serverCommand $serverArguments $agentDirectory @{} '/session/output/transcript.jsonl'
    try { $sessionClient.Tools | ConvertTo-Json -Depth 40 -AsArray | Set-Content /session/output/tools.json }
    finally { [void](Close-ABMcpSession $sessionClient) }
    exit
}
$hostStarted = [DateTimeOffset]::UtcNow
$hostClock = [Diagnostics.Stopwatch]::StartNew()
$hostRun = $null
$hostData = $null
if ($manifest.arm.host -eq 'fake') {
    # A script names built-in tools; the profile's renames let it drive any profile file.
    $toolRenames = [ordered]@{}
    $servedProfile = Read-ABJson $manifest.profile
    $selections = if ($servedProfile.Contains('tools')) { @($servedProfile.tools) } else { @() }
    foreach ($selection in $selections) {
        if ($selection -and $selection.Contains('as') -and $selection.as) { $toolRenames[[string]$selection.tool] = [string]$selection.as }
    }
    $fakeManifest = [ordered]@{
        server = [ordered]@{ command = $hostPlan.bridge; arguments = $hostPlan.bridgeArguments }
        childEnvironment = $hostPlan.environment
        agentDirectory = '/workspace/work'
        fakeScript = '/opt/client/actions.jsonl'
        task = $task
        profileName = $manifest.profileName
        toolRenames = $toolRenames
        transcriptPath = '-'
        hostResultPath = '/workspace/state/host.json'
        mcpTimeoutMs = $manifest.mcpTimeoutMs
        project = Get-ABProjectName
    }
    Write-ABJson '/session/agent/config/session.json' $fakeManifest
}
$hostRun = Invoke-ABBudgetedAgent '/usr/bin/python3' @('/opt/client/agent-boundary.py', $agentLaunchPath) $agentDirectory $agentBudget `
    '/session/output/mcp-transcript.jsonl' '/session/output/budget.json'
if ($manifest.arm.host -eq 'fake') {
    foreach ($line in @($hostRun.Stdout -split "`r?`n" | Where-Object { $_ })) {
        Add-ABJsonl $transcriptPath ([ordered]@{ source = 'host-output'; stream = 'stdout'; payload = $line })
    }
    foreach ($line in @($hostRun.Stderr -split "`r?`n" | Where-Object { $_ })) {
        Add-ABJsonl $transcriptPath ([ordered]@{ source = 'host-output'; stream = 'stderr'; payload = $line })
    }
    $agentResult = '/session/agent/state/host.json'
    $hostData = if (Test-Path $agentResult) { Read-ABJson $agentResult } else {
        [ordered]@{ host = 'fake'; exitCode = $hostRun.ExitCode; wallMs = $hostRun.WallMs; finalMessage = $hostRun.Stderr; toolCalls = @(); turns = 0 }
    }
} else {
    $finalPath = '/session/agent/state/last-message.txt'
    $outputLines = @($hostRun.Stdout -split "`r?`n" | Where-Object { $_ })
    foreach ($line in $outputLines) {
        Add-ABJsonl $transcriptPath ([ordered]@{ atUtc = [DateTimeOffset]::UtcNow.ToString('O'); source = 'host-output'; stream = 'stdout'; payload = $line })
    }
    foreach ($line in @($hostRun.Stderr -split "`r?`n" | Where-Object { $_ })) {
        Add-ABJsonl $transcriptPath ([ordered]@{ atUtc = [DateTimeOffset]::UtcNow.ToString('O'); source = 'host-output'; stream = 'stderr'; payload = $line })
    }
    $events = @($outputLines | ForEach-Object { try { $_ | ConvertFrom-Json -AsHashtable } catch { $null } } | Where-Object { $_ })
    $toolAttempts = @(Get-ABToolAttempts '/session/output/mcp-transcript.jsonl' @())
    $inputTokens = $hostRun.InputTokens
    $outputTokens = $hostRun.OutputTokens
    $costUsd = $null
    $turns = 0
    $finalMessage = if (Test-Path -LiteralPath $finalPath) { Get-Content -LiteralPath $finalPath -Raw } else { '' }
    foreach ($event in $events) {
        $eventType = if ($event.Contains('type')) { [string]$event.type } else { '' }
        if ($eventType -eq 'turn.completed') { $turns++ }
        if ($event.Contains('num_turns') -and $null -ne $event.num_turns) {
            $turns = [Math]::Max($turns, [int]$event.num_turns)
        }
        if ($event.Contains('total_cost_usd') -and $null -ne $event.total_cost_usd) {
            $costUsd = [double]$event.total_cost_usd
        }
        $usageRecords = [Collections.Generic.List[object]]::new()
        if ($event.Contains('usage') -and $event.usage) { $usageRecords.Add($event.usage) }
        if ($event.Contains('message') -and $event.message -is [System.Collections.IDictionary] -and
            $event.message.Contains('usage') -and $event.message.usage) { $usageRecords.Add($event.message.usage) }
        foreach ($usage in $usageRecords) {
            if ($usage -isnot [System.Collections.IDictionary]) { continue }
            if ($usage.Contains('input_tokens') -and $null -ne $usage.input_tokens) {
                $inputTokens = [Math]::Max([int]$inputTokens, [int]$usage.input_tokens)
            }
            if ($usage.Contains('output_tokens') -and $null -ne $usage.output_tokens) {
                $outputTokens = [Math]::Max([int]$outputTokens, [int]$usage.output_tokens)
            }
        }
        if ($eventType -eq 'result' -and $event.Contains('result') -and $event.result) {
            $finalMessage = [string]$event.result
        }
        if ($eventType -eq 'assistant' -and $event.Contains('message') -and
            $event.message -is [System.Collections.IDictionary] -and $event.message.Contains('content') -and
            $event.message.content) {
            $texts = @($event.message.content | Where-Object { $_.type -eq 'text' } | ForEach-Object { $_.text })
            if ($texts.Count -gt 0) { $finalMessage = $texts -join "`n" }
        }
    }
    if ($turns -eq 0) { $turns = @($events | Where-Object { $_.Contains('type') -and $_.type -eq 'assistant' }).Count }
    $turns = [Math]::Max($turns, [int]$hostRun.Turns)
    $hostData = [ordered]@{
        host = [string]$manifest.arm.host
        startedUtc = $hostStarted.ToString('O')
        finishedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        wallMs = $hostRun.WallMs
        exitCode = $hostRun.ExitCode
        turns = $turns
        toolCalls = @($toolAttempts)
        finalMessage = $finalMessage
        failure = if ($hostRun.Failure) { $hostRun.Failure } elseif ($hostRun.ExitCode -ne 0) { $hostRun.Stderr } else { $null }
        inputTokens = $inputTokens
        outputTokens = $outputTokens
        costUsd = $costUsd
        agentBudget = $hostRun.AgentBudget
        agentToolCalls = $hostRun.ToolCalls
        failureClass = $hostRun.FailureClass
    }
}

$hostClock.Stop()
$hostData.agentBudget = $hostRun.AgentBudget
$hostData.agentToolCalls = $hostRun.ToolCalls
if ($hostRun.FailureClass) { $hostData.failureClass = $hostRun.FailureClass }
$hostData.primaryMetric = ''
Write-ABJson $hostResultPath $hostData
Add-ABJsonl $transcriptPath ([ordered]@{ atUtc = [DateTimeOffset]::UtcNow.ToString('O'); source = 'host-summary'; payload = $hostData })
if ($hostRun.TimedOut) { exit 124 }
exit $hostData.exitCode
