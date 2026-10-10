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
