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
$serverArguments = @('mcp', '--project', $fwDataPath, '--profile', $manifest.profile, '--activity-log', $activityPath)
$systemAppend = $manifest.systemAppend
$hostPlan = New-ABHostPlan $manifest.arm ([string]$task.promptText) $task.limits $systemAppend

function Invoke-ABBudgetedAgent([string] $Executable, [string[]] $Arguments, [string] $Directory, [object] $Limits) {
    $configuredWallSeconds = if ($Limits.Contains('wall_seconds')) { [int]$Limits.wall_seconds } else { 1800 }
    $timeSeconds = if ($Limits.Contains('time_seconds')) { [int]$Limits.time_seconds } else { [Math]::Max(1, [int][Math]::Floor($configuredWallSeconds / 3)) }
    $wallSeconds = $timeSeconds * 3
    $turnBudget = if ($Limits.Contains('turns')) { [int]$Limits.turns } else { 60 }
    $tokenBudget = if ($Limits.Contains('tokens')) { [int]$Limits.tokens } elseif ($env:MOTIF_AGENT_TOKEN_BUDGET) {
        [int]$env:MOTIF_AGENT_TOKEN_BUDGET
    } else { 100000 }
    if ($timeSeconds -lt 1 -or $turnBudget -lt 1 -or $tokenBudget -lt 1 -or $configuredWallSeconds -lt $wallSeconds) {
        throw 'Agent limits must have positive time, turn and token budgets and a wall cap at least three times time.'
    }
    $startInfo = [Diagnostics.ProcessStartInfo]::new($Executable)
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.CreateNoWindow = $true
    $startInfo.WorkingDirectory = $Directory
    foreach ($argument in $Arguments) { [void]$startInfo.ArgumentList.Add([string]$argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $clock = [Diagnostics.Stopwatch]::StartNew()
    if (-not $process.Start()) { throw "Could not start '$Executable'." }
    $process.StandardInput.Close()
    $stdoutLines = [Collections.Generic.List[string]]::new()
    $stderrLines = [Collections.Generic.List[string]]::new()
    $stdoutTask = $process.StandardOutput.ReadLineAsync()
    $stderrTask = $process.StandardError.ReadLineAsync()
    $stdoutDone = $false
    $stderrDone = $false
    $lastOutputMs = 0L
    $lastAccountedMs = 0L
    $turns = 0
    $activeElapsedMs = 0L
    $inputTokens = 0
    $outputTokens = 0
    $usageByTurn = @{}
    $failureClass = $null
    $failure = $null
    while (-not ($process.HasExited -and $stdoutDone -and $stderrDone)) {
        if (-not $stdoutDone -and $stdoutTask.IsCompleted) {
            $line = $stdoutTask.GetAwaiter().GetResult()
            if ($null -eq $line) { $stdoutDone = $true }
            else {
                $stdoutLines.Add($line)
                $outputAt = $clock.ElapsedMilliseconds
                $quietGap = $outputAt - $lastOutputMs
                if ($quietGap -ge 120000) {
                    $failureClass = 'cloud_failure'
                    $failure = 'The model service produced no host output for 120 seconds.'
                }
                $lastOutputMs = $outputAt
                try {
                    $event = $line | ConvertFrom-Json -AsHashtable
                    if ($event.type -eq 'turn.completed') { $turns++ }
                    if ($event.Contains('num_turns') -and $null -ne $event.num_turns) { $turns = [Math]::Max($turns, [int]$event.num_turns) }
                    $usageKey = if ($event.Contains('turn_id') -and $event.turn_id) { 'turn-' + [string]$event.turn_id }
                        elseif ($event.Contains('num_turns') -and $null -ne $event.num_turns) { 'turn-' + [string]$event.num_turns }
                        elseif ($event.Contains('message') -and $event.message -is [System.Collections.IDictionary] -and $event.message.Contains('id')) {
                            'message-' + [string]$event.message.id
                        } else { 'turn-' + [string][Math]::Max(1, $turns) }
                    $usageRows = [Collections.Generic.List[object]]::new()
                    if ($event.Contains('usage') -and $event.usage) { $usageRows.Add($event.usage) }
                    if ($event.Contains('message') -and $event.message -is [System.Collections.IDictionary] -and
                        $event.message.Contains('usage') -and $event.message.usage) { $usageRows.Add($event.message.usage) }
                    foreach ($usage in $usageRows) {
                        if (-not $usageByTurn.ContainsKey($usageKey)) { $usageByTurn[$usageKey] = @{ input = 0; output = 0 } }
                        if ($usage.Contains('input_tokens')) { $usageByTurn[$usageKey].input = [Math]::Max($usageByTurn[$usageKey].input, [int]$usage.input_tokens) }
                        if ($usage.Contains('output_tokens')) { $usageByTurn[$usageKey].output = [Math]::Max($usageByTurn[$usageKey].output, [int]$usage.output_tokens) }
                    }
                } catch { }
                $stdoutTask = $process.StandardOutput.ReadLineAsync()
            }
        }
        if (-not $stderrDone -and $stderrTask.IsCompleted) {
            $line = $stderrTask.GetAwaiter().GetResult()
            if ($null -eq $line) { $stderrDone = $true }
            else {
                $stderrLines.Add($line)
                $outputAt = $clock.ElapsedMilliseconds
                $quietGap = $outputAt - $lastOutputMs
                if ($quietGap -ge 120000) {
                    $failureClass = 'cloud_failure'
                    $failure = 'The model service produced no host output for 120 seconds.'
                }
                $lastOutputMs = $outputAt
                $stderrTask = $process.StandardError.ReadLineAsync()
            }
        }
        $elapsed = $clock.ElapsedMilliseconds
        $inputTokens = [int](($usageByTurn.Values | ForEach-Object { [int]$_.input } | Measure-Object -Sum).Sum)
        $outputTokens = [int](($usageByTurn.Values | ForEach-Object { [int]$_.output } | Measure-Object -Sum).Sum)
        if (-not $process.HasExited -and $elapsed - $lastOutputMs -ge 120000) {
            $failureClass = 'cloud_failure'
            $failure = 'The model service produced no host output for 120 seconds.'
        } else {
            $activeElapsedMs += $elapsed - $lastAccountedMs
            $lastAccountedMs = $elapsed
        }
        if (-not $failureClass -and -not $process.HasExited -and $activeElapsedMs -ge $timeSeconds * 1000) {
            $failureClass = 'agent_failure'
            $failure = "The Agent budget of $timeSeconds seconds was exhausted."
        } elseif (-not $failureClass -and -not $process.HasExited -and $turns -gt $turnBudget) {
            $failureClass = 'agent_failure'
            $failure = "The Agent budget of $turnBudget turns was exceeded."
        } elseif (-not $failureClass -and -not $process.HasExited -and $inputTokens + $outputTokens -gt $tokenBudget) {
            $failureClass = 'agent_failure'
            $failure = "The Agent budget of $tokenBudget tokens was exceeded."
        } elseif (-not $failureClass -and -not $process.HasExited -and $elapsed -ge $wallSeconds * 1000) {
            $failureClass = 'cloud_failure'
            $failure = "The Episode reached the wall cap of $wallSeconds seconds before the active Agent time budget was used."
        }
        if ($failureClass -and -not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        if (-not ($process.HasExited -and $stdoutDone -and $stderrDone)) { Start-Sleep -Milliseconds 20 }
    }
    $clock.Stop()
    if ($clock.ElapsedMilliseconds - $lastOutputMs -ge 120000) {
        $failureClass = 'cloud_failure'
        $failure = 'The model service produced no host output for 120 seconds.'
    }
    $inputTokens = [int](($usageByTurn.Values | ForEach-Object { [int]$_.input } | Measure-Object -Sum).Sum)
    $outputTokens = [int](($usageByTurn.Values | ForEach-Object { [int]$_.output } | Measure-Object -Sum).Sum)
    return [pscustomobject]@{
        ExitCode = if ($failureClass) { 124 } else { $process.ExitCode }
        TimedOut = [bool]$failureClass
        WallMs = $clock.ElapsedMilliseconds
        AgentElapsedMs = $activeElapsedMs
        Stdout = $stdoutLines -join "`n"
        Stderr = $stderrLines -join "`n"
        Turns = $turns
        InputTokens = $inputTokens
        OutputTokens = $outputTokens
        FailureClass = $failureClass
        Failure = $failure
        AgentBudget = @{ timeSeconds = $timeSeconds; turns = $turnBudget; tokens = $tokenBudget; wallCapSeconds = $wallSeconds }
    }
}

$agentLaunchPath = Join-Path $privateRoot 'agent-launch.json'
foreach ($path in @('/session/agent/config', '/session/agent/state')) { New-Item -ItemType Directory $path -Force | Out-Null }
Write-ABJson '/session/agent/config/mcp.json' $hostPlan.mcpConfig
Write-ABJson $agentLaunchPath @{ plan = $hostPlan; host = $manifest.arm.host; serverCommand = $serverCommand
    serverArguments = $serverArguments; preflight = [bool]$manifest.preflight }
foreach ($path in @($privateRoot, $agentDirectory, $workerRoot)) { New-Item -ItemType Directory $path -Force | Out-Null }
$baseline = Invoke-ABProcess $motifExe @('baseline', 'capture', $fwDataPath, '--json') $agentDirectory @{} 300000
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
    $fakeManifest = [ordered]@{
        server = [ordered]@{ command = $hostPlan.bridge; arguments = $hostPlan.bridgeArguments }
        childEnvironment = $hostPlan.environment
        agentDirectory = '/workspace/work'
        fakeScript = '/opt/client/actions.jsonl'
        task = $task
        profileName = $manifest.profileName
        transcriptPath = '-'
        hostResultPath = '/workspace/state/host.json'
        mcpTimeoutMs = $manifest.mcpTimeoutMs
    }
    Write-ABJson '/session/agent/config/session.json' $fakeManifest
}
$hostRun = Invoke-ABBudgetedAgent '/usr/bin/python3' @('/opt/client/agent-boundary.py', $agentLaunchPath) $agentDirectory $task.limits
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
        agentElapsedMs = $hostRun.AgentElapsedMs
        failureClass = $hostRun.FailureClass
    }
}

$hostClock.Stop()
$hostData.agentBudget = $hostRun.AgentBudget
$hostData.agentElapsedMs = $hostRun.AgentElapsedMs
if ($hostRun.FailureClass) { $hostData.failureClass = $hostRun.FailureClass }
$hostData.primaryMetric = ''
Write-ABJson $hostResultPath $hostData
Add-ABJsonl $transcriptPath ([ordered]@{ atUtc = [DateTimeOffset]::UtcNow.ToString('O'); source = 'host-summary'; payload = $hostData })
if ($hostRun.TimedOut) { exit 124 }
exit $hostData.exitCode
