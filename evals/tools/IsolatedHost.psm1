Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Read-ABJson([string] $Path) {
    return (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable)
}

function Write-ABJson([string] $Path, [object] $Value) {
    $parent = Split-Path -Parent $Path
    if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    $json = $Value | ConvertTo-Json -Depth 80
    Set-Content -LiteralPath $Path -Value $json -Encoding utf8
}

function Add-ABJsonl([string] $Path, [object] $Value) {
    $line = $Value | ConvertTo-Json -Depth 80 -Compress
    if ($Path -eq '-') { [Console]::Out.WriteLine($line) }
    else { [IO.File]::AppendAllText($Path, $line + [Environment]::NewLine, [Text.UTF8Encoding]::new($false)) }
}

function Format-ABPosixArgument([string] $Value) {
    if ($Value -match '^[A-Za-z0-9_./:=+-]+$') { return $Value }
    return "'" + $Value.Replace("'", "'\''") + "'"
}

function Format-ABCommand([string] $Executable, [string[]] $Arguments) {
    return (@($Executable) + @($Arguments) | ForEach-Object { Format-ABPosixArgument ([string]$_) }) -join ' '
}

function Invoke-ABProcess(
    [string] $Executable,
    [string[]] $Arguments,
    [string] $WorkingDirectory,
    [hashtable] $Environment,
    [int] $TimeoutMs
) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new($Executable)
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.CreateNoWindow = $true
    if ($WorkingDirectory) { $startInfo.WorkingDirectory = $WorkingDirectory }
    foreach ($argument in $Arguments) { [void]$startInfo.ArgumentList.Add([string]$argument) }
    foreach ($key in $Environment.Keys) { $startInfo.Environment[[string]$key] = [string]$Environment[$key] }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $clock = [Diagnostics.Stopwatch]::StartNew()
    if (-not $process.Start()) { throw "Could not start '$Executable'." }
    $process.StandardInput.Close()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $timedOut = -not $process.WaitForExit($TimeoutMs)
    if ($timedOut) {
        $process.Kill($true)
        $process.WaitForExit()
    }
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $clock.Stop()
    return [pscustomobject]@{
        ExitCode = if ($timedOut) { 124 } else { $process.ExitCode }
        TimedOut = $timedOut
        WallMs = $clock.ElapsedMilliseconds
        Stdout = $stdout
        Stderr = $stderr
    }
}

function Read-ABLines([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    $records = [Collections.Generic.List[object]]::new()
    foreach ($line in Get-Content -LiteralPath $Path) {
        if (-not [string]::IsNullOrWhiteSpace($line)) {
            try { $records.Add(($line | ConvertFrom-Json -AsHashtable)) }
            catch { $records.Add([ordered]@{ raw = $line }) }
        }
    }
    return @($records)
}

function Add-ABToolAttempt([Collections.Generic.List[object]] $Attempts, [string] $Name, [object] $Arguments,
    [string] $CallId = '') {
    if ([string]::IsNullOrWhiteSpace($Name)) { return }
    $normalized = $Name
    if ($normalized.StartsWith('mcp__motif__', [StringComparison]::Ordinal)) {
        $normalized = $normalized.Substring('mcp__motif__'.Length)
    }
    elseif ($normalized.StartsWith('motif/', [StringComparison]::Ordinal)) {
        $normalized = $normalized.Substring('motif/'.Length)
    }
    $Attempts.Add([ordered]@{ name = $normalized; rawName = $Name; arguments = $Arguments; callId = $CallId })
}

function Visit-ABToolNode([object] $Node, [Collections.Generic.List[object]] $Attempts) {
    if ($null -eq $Node) { return }
    if ($Node -is [System.Collections.IDictionary]) {
        $nodeType = if ($Node.Contains('type')) { [string]$Node.type } else { '' }
        if ($nodeType -eq 'tool_use' -and $Node.Contains('name') -and $Node.name) {
            Add-ABToolAttempt $Attempts ([string]$Node.name) $Node.input
        }
        elseif ($nodeType -eq 'function_call' -and $Node.Contains('name') -and $Node.name) {
            Add-ABToolAttempt $Attempts ([string]$Node.name) $Node.arguments
        }
        elseif ($nodeType -eq 'mcp_tool_call' -and ($Node.Contains('tool') -or $Node.Contains('name'))) {
            $toolName = if ($Node.Contains('tool')) { [string]$Node.tool } else { [string]$Node.name }
            $arguments = if ($Node.Contains('arguments')) { $Node.arguments } elseif ($Node.Contains('input')) { $Node.input } else { @{} }
            $callId = if ($Node.Contains('id')) { [string]$Node.id } elseif ($Node.Contains('call_id')) { [string]$Node.call_id } else { '' }
            Add-ABToolAttempt $Attempts $toolName $arguments $callId
            return
        }
        elseif ($Node.Contains('method') -and $Node.method -eq 'tools/call' -and $Node.Contains('params') -and $Node.params.name) {
            Add-ABToolAttempt $Attempts ([string]$Node.params.name) $Node.params.arguments
        }
        foreach ($key in $Node.Keys) { Visit-ABToolNode $Node[$key] $Attempts }
    }
    elseif ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) {
        foreach ($item in $Node) { Visit-ABToolNode $item $Attempts }
    }
    elseif ($Node -is [string] -and $Node.StartsWith('{')) {
        try { Visit-ABToolNode ($Node | ConvertFrom-Json -AsHashtable) $Attempts }
        catch { }
    }
}

function Get-ABToolAttempts([string] $TranscriptPath, [object[]] $Activity) {
    $attempts = [Collections.Generic.List[object]]::new()
    foreach ($record in Read-ABLines $TranscriptPath) {
        $payload = if ($record.Contains('payload')) { $record.payload } else { $null }
        if ($record.source -eq 'mcp-client' -and $record.direction -eq 'sent' -and
            $payload -is [System.Collections.IDictionary] -and $payload.Contains('method') -and
            $payload.method -eq 'tools/call' -and $payload.Contains('params') -and $payload.params.name) {
            Add-ABToolAttempt $attempts ([string]$payload.params.name) $payload.params.arguments
        }
        if ($record.source -eq 'host-output') { Visit-ABToolNode $record.payload $attempts }
    }
    $uniqueAttempts = [Collections.Generic.List[object]]::new()
    $seenCallIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($attempt in $attempts) {
        if ($attempt.callId -and -not $seenCallIds.Add([string]$attempt.callId)) { continue }
        $uniqueAttempts.Add($attempt)
    }
    $attempts = $uniqueAttempts
    $remaining = @{}
    foreach ($attempt in $attempts) {
        $key = [string]$attempt.name + "`n" + ($attempt.arguments | ConvertTo-Json -Depth 40 -Compress)
        if (-not $remaining.ContainsKey($key)) { $remaining[$key] = 0 }
        $remaining[$key]++
    }
    foreach ($item in $Activity) {
        $name = if ($item.Contains('name')) { [string]$item.name } elseif ($item.Contains('tool')) { [string]$item.tool } else { '' }
        if (-not $name) { continue }
        $arguments = if ($item.Contains('arguments')) { $item.arguments } elseif ($item.Contains('args')) { $item.args } else { @{} }
        $key = $name + "`n" + ($arguments | ConvertTo-Json -Depth 40 -Compress)
        if ($remaining.ContainsKey($key) -and $remaining[$key] -gt 0) { $remaining[$key]--; continue }
        Add-ABToolAttempt $attempts $name $arguments
    }
    return @($attempts)
}

function Get-ABAgentBudget([object] $Limits) {
    if ($Limits.Contains('time_seconds')) {
        throw 'Task limits name time_seconds, but time is not an Agent budget: use turns, tool_calls or tokens.'
    }
    $tokens = if ($Limits.Contains('tokens')) { [long]$Limits.tokens } elseif ($env:MOTIF_AGENT_TOKEN_BUDGET) {
        [long]$env:MOTIF_AGENT_TOKEN_BUDGET
    } else { 1500000 }
    $budget = [ordered]@{
        turns = if ($Limits.Contains('turns')) { [int]$Limits.turns } else { 60 }
        # No default: only a task that names tool_calls caps them.
        toolCalls = if ($Limits.Contains('tool_calls')) { [int]$Limits.tool_calls } else { $null }
        tokens = $tokens
        # A loaded host may run an Episode several times slower; the kill only stops one that never ends.
        safetySeconds = [Math]::Max(3600, $(if ($Limits.Contains('wall_seconds')) { [int]$Limits.wall_seconds } else { 0 }))
    }
    if ($budget.turns -lt 1 -or ($null -ne $budget.toolCalls -and $budget.toolCalls -lt 1) -or $budget.tokens -lt 1) {
        throw 'Agent limits must have positive turn, tool-call and token budgets.'
    }
    return $budget
}

function Get-ABBudgetedToolCall([string] $Line) {
    try { $record = $Line | ConvertFrom-Json -AsHashtable } catch { return $false }
    if ($record -isnot [System.Collections.IDictionary] -or $record.direction -ne 'sent') { return $false }
    $payload = $record.payload
    if ($payload -isnot [System.Collections.IDictionary] -or $payload.method -ne 'tools/call') { return $false }
    $arguments = if ($payload.Contains('params') -and $payload.params.Contains('arguments')) { $payload.params.arguments } else { $null }
    # Re-asking for a running job is Motif's wait protocol; a slower host would otherwise spend more of the budget.
    return -not ($arguments -is [System.Collections.IDictionary] -and $arguments.Contains('job') -and $arguments.job)
}

function Invoke-ABBudgetedAgent([string] $Executable, [string[]] $Arguments, [string] $Directory, [object] $Budget,
    [string] $ToolTranscriptPath, [string] $BudgetRecordPath) {
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
    $turns = 0
    $toolCalls = 0
    $toolOffset = 0L
    $toolPartial = ''
    $usageByTurn = @{}
    $failureClass = $null
    $failure = $null
    while (-not ($process.HasExited -and $stdoutDone -and $stderrDone)) {
        if (-not $stdoutDone -and $stdoutTask.IsCompleted) {
            $line = $stdoutTask.GetAwaiter().GetResult()
            if ($null -eq $line) { $stdoutDone = $true }
            else {
                $stdoutLines.Add($line)
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
                        if (-not $usageByTurn.ContainsKey($usageKey)) { $usageByTurn[$usageKey] = @{ input = 0L; output = 0L } }
                        # Claude reports cached input apart from input_tokens; Codex includes it, so both count it once.
                        $input = 0L
                        foreach ($name in @('input_tokens', 'cache_read_input_tokens', 'cache_creation_input_tokens')) {
                            if ($usage.Contains($name) -and $null -ne $usage[$name]) { $input += [long]$usage[$name] }
                        }
                        $usageByTurn[$usageKey].input = [Math]::Max($usageByTurn[$usageKey].input, $input)
                        if ($usage.Contains('output_tokens') -and $null -ne $usage.output_tokens) {
                            $usageByTurn[$usageKey].output = [Math]::Max($usageByTurn[$usageKey].output, [long]$usage.output_tokens)
                        }
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
                $stderrTask = $process.StandardError.ReadLineAsync()
            }
        }
        if ($ToolTranscriptPath -and (Test-Path -LiteralPath $ToolTranscriptPath)) {
            $stream = [IO.FileStream]::new($ToolTranscriptPath, 'Open', 'Read', 'ReadWrite')
            try {
                if ($stream.Length -gt $toolOffset) {
                    [void]$stream.Seek($toolOffset, 'Begin')
                    $reader = [IO.StreamReader]::new($stream)
                    $text = $toolPartial + $reader.ReadToEnd()
                    $toolOffset = $stream.Length
                    $parts = $text -split "`n"
                    $toolPartial = $parts[-1]
                    foreach ($part in $parts[0..($parts.Count - 2)]) { if (Get-ABBudgetedToolCall $part) { $toolCalls++ } }
                }
            } finally { $stream.Dispose() }
        }
        # The broker and the bridge refuse work past the budget so the host ends itself; only the safety kill stops it.
        if (-not $failureClass -and -not $process.HasExited -and $clock.ElapsedMilliseconds -ge [long]$Budget.safetySeconds * 1000) {
            $failureClass = 'harness_defect'
            $failure = "The Episode reached the safety wall kill of $($Budget.safetySeconds) seconds."
            $process.Kill($true); $process.WaitForExit()
        }
        if (-not ($process.HasExited -and $stdoutDone -and $stderrDone)) { Start-Sleep -Milliseconds 20 }
    }
    $clock.Stop()
    $tokens = [long](($usageByTurn.Values | ForEach-Object { $_.input + $_.output } | Measure-Object -Sum).Sum)
    if (-not $failureClass -and $BudgetRecordPath -and (Test-Path -LiteralPath $BudgetRecordPath)) {
        $failureClass = 'agent_failure'; $failure = [string](Read-ABJson $BudgetRecordPath).exhausted
    } elseif (-not $failureClass -and $turns -gt $Budget.turns) {
        $failureClass = 'agent_failure'; $failure = "The Agent budget of $($Budget.turns) turns was exceeded."
    } elseif (-not $failureClass -and $null -ne $Budget.toolCalls -and $toolCalls -gt $Budget.toolCalls) {
        $failureClass = 'agent_failure'; $failure = "The Agent budget of $($Budget.toolCalls) tool calls was exceeded."
    } elseif (-not $failureClass -and $tokens -gt $Budget.tokens) {
        $failureClass = 'agent_failure'; $failure = "The Agent budget of $($Budget.tokens) tokens was exceeded."
    }
    $inputTokens = [long](($usageByTurn.Values | ForEach-Object { $_.input } | Measure-Object -Sum).Sum)
    $outputTokens = [long](($usageByTurn.Values | ForEach-Object { $_.output } | Measure-Object -Sum).Sum)
    return [pscustomobject]@{
        ExitCode = if ($failureClass) { 124 } else { $process.ExitCode }
        TimedOut = [bool]$failureClass
        WallMs = $clock.ElapsedMilliseconds
        Stdout = $stdoutLines -join "`n"
        Stderr = $stderrLines -join "`n"
        Turns = $turns
        ToolCalls = $toolCalls
        Tokens = $inputTokens + $outputTokens
        InputTokens = $inputTokens
        OutputTokens = $outputTokens
        FailureClass = $failureClass
        Failure = $failure
        AgentBudget = $Budget
    }
}
