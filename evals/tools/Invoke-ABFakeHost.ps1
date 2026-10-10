[CmdletBinding()]
param([Parameter(Mandatory)][string] $RunManifest)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath $RunManifest -Raw | ConvertFrom-Json -AsHashtable
$modulePath = Join-Path $PSScriptRoot 'protocol.psm1'
Import-Module $modulePath -Force
$clock = [Diagnostics.Stopwatch]::StartNew()
$startedUtc = [DateTimeOffset]::UtcNow
$calls = [Collections.Generic.List[object]]::new()
$finalMessage = ''
$exitCode = 0
$failure = $null
$failureKind = $null
$toolName = $null
$mcpTimeoutMs = 200000
$session = $null
$variables = @{}

function Get-ABPathValue([object] $Value, [string] $Path) {
    $current = $Value
    foreach ($segment in $Path.Split('.')) {
        $match = [regex]::Match($segment, '^(?<name>[^\[]+)(?:\[(?<selector>[^\]]+)\])?$')
        if (-not $match.Success) { throw "Invalid fake-script capture path '$Path'." }
        $name = $match.Groups['name'].Value
        if ($current -is [System.Collections.IDictionary]) { $current = $current[$name] }
        else { $current = $current.$name }
        if ($null -eq $current) { return $null }
        $selector = $match.Groups['selector'].Value
        if (-not $selector) { continue }
        if ($selector -match '^\d+$') {
            $current = $current[[int]$selector]
        } elseif ($current -is [System.Collections.IEnumerable] -and $current -isnot [string]) {
            $predicate = [regex]::Match($selector, '^(?<key>[^=]+)=(?<value>.*)$')
            if (-not $predicate.Success) { throw "Invalid fake-script selector '$selector'." }
            $key = $predicate.Groups['key'].Value
            $expected = $predicate.Groups['value'].Value
            $current = @($current | Where-Object { [string]$_[$key] -ieq $expected } | Select-Object -First 1)[0]
        } else { return $null }
    }
    return $current
}

function Resolve-ABScriptValue([object] $Value) {
    if ($Value -is [string]) {
        $match = [regex]::Match($Value, '^\$\{(?<name>[A-Za-z][A-Za-z0-9_]*)\}$')
        if ($match.Success) {
            $name = $match.Groups['name'].Value
            if (-not $script:variables.ContainsKey($name)) { throw "Fake-script variable '$name' has not been captured." }
            return $script:variables[$name]
        }
        return $Value
    }
    if ($Value -is [System.Collections.IDictionary]) {
        $resolved = @{}
        foreach ($key in $Value.Keys) { $resolved[$key] = Resolve-ABScriptValue $Value[$key] }
        return $resolved
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        return @($Value | ForEach-Object { Resolve-ABScriptValue $_ })
    }
    return $Value
}
try {
    if ($manifest.Contains('mcpTimeoutMs')) { $mcpTimeoutMs = [int]$manifest.mcpTimeoutMs }
    $session = New-ABMcpSession -Command $manifest.server.command -Arguments @($manifest.server.arguments) `
        -WorkingDirectory $manifest.agentDirectory -Environment $manifest.childEnvironment `
        -TranscriptPath $manifest.transcriptPath -ToolTimeoutMs $mcpTimeoutMs
    foreach ($entry in Get-Content -LiteralPath $manifest.fakeScript) {
        if ([string]::IsNullOrWhiteSpace($entry)) { continue }
        if ($calls.Count -ge $manifest.task.limits.turns) { throw 'The fake script exceeded the task turn limit.' }
        $scriptCall = $entry | ConvertFrom-Json -AsHashtable
        $profileIndex = [Array]::IndexOf([string[]]$manifest.server.arguments, '--profile')
        $profileName = if ($manifest.Contains('profileName')) { [string]$manifest.profileName } elseif ($profileIndex -ge 0) { [string]$manifest.server.arguments[$profileIndex + 1] } else { '' }
        if ($scriptCall.Contains('skipProfiles') -and $profileName -in @($scriptCall.skipProfiles)) { continue }
        if ($scriptCall.Contains('finalMessage')) {
            $finalMessage = [string]$scriptCall.finalMessage
            continue
        }
        if ($scriptCall.Contains('exec')) {
            $probe = [Diagnostics.ProcessStartInfo]::new([string]$scriptCall.exec)
            $probe.UseShellExecute = $false
            $probe.RedirectStandardOutput = $true
            $probe.RedirectStandardError = $true
            foreach ($argument in @($scriptCall.args)) { [void]$probe.ArgumentList.Add([string]$argument) }
            try {
                $process = [Diagnostics.Process]::Start($probe)
                $stdoutTask = $process.StandardOutput.ReadToEndAsync()
                $stderrTask = $process.StandardError.ReadToEndAsync()
                if (-not $process.WaitForExit(10000)) { $process.Kill($true); $process.WaitForExit() }
                $probeOutput = $stdoutTask.GetAwaiter().GetResult()
                $probeError = $stderrTask.GetAwaiter().GetResult()
                Add-ABTranscriptLine $manifest.transcriptPath ([ordered]@{
                    source = 'host-output'; payload = @{ type = 'probe'; command = $scriptCall.exec; exitCode = $process.ExitCode; output = $probeOutput; error = $probeError }
                })
            } catch {
                Add-ABTranscriptLine $manifest.transcriptPath ([ordered]@{ source = 'host-output'; payload = @{ type = 'probe'; command = $scriptCall.exec; denied = $true } })
            }
            continue
        }
        $toolName = [string]$scriptCall.tool
        if ($toolName -eq 'motif_start_proposal' -and $profileName -eq 'lean') {
            $toolName = 'motif_new_proposal'
        }
        $toolArguments = if ($scriptCall.arguments -is [System.Collections.IDictionary]) {
            Resolve-ABScriptValue $scriptCall.arguments
        } else { @{} }
        try { $result = Invoke-ABMcpTool $session $toolName $toolArguments }
        catch {
            if ($_.Exception -isnot [TimeoutException] -and $scriptCall.Contains('expectDenied') -and $scriptCall.expectDenied) { continue }
            throw
        }
        $toolResult = $result.result
        $toolIsError = $toolResult.Contains('isError') -and [bool]$toolResult.isError
        if ($toolIsError) {
            $errorText = if ($toolResult.content.Count -gt 0) { [string]$toolResult.content[0].text } else { 'MCP tool returned an error.' }
            if ($scriptCall.Contains('expectDenied') -and $scriptCall.expectDenied) { continue }
            throw "Fake script tool '$toolName' failed: $errorText"
        }
        $structured = if ($toolResult.Contains('structuredContent')) { $toolResult.structuredContent } else { $null }
        if (-not $structured -and $toolResult.content.Count -gt 0) {
            $contentText = ([string]$toolResult.content[0].text -split '\nNext:', 2)[0]
            try { $structured = $contentText | ConvertFrom-Json -AsHashtable } catch { }
        }
        if ($scriptCall.Contains('capture')) {
            foreach ($name in $scriptCall.capture.Keys) {
                $variables[[string]$name] = Get-ABPathValue $structured.result ([string]$scriptCall.capture[$name])
                if ($null -eq $variables[[string]$name]) { throw "Fake script could not capture '$name'." }
            }
        }
        $calls.Add([ordered]@{ name = $toolName; arguments = $toolArguments; isError = $toolIsError })
        foreach ($content in @($toolResult.content)) {
            if ($content.type -eq 'text' -and $content.text) { $finalMessage = [string]$content.text }
        }
    }
}
catch {
    $failure = $_.Exception.Message
    $failureKind = if ($_.Exception -is [TimeoutException]) { 'mcp_timeout' } else { 'host_error' }
    $exitCode = 1
    $finalMessage = $failure
}
finally {
    $serverStderr = if ($session) { Close-ABMcpSession $session } else { '' }
}
$clock.Stop()
$hostResult = [ordered]@{
    host = 'fake'
    startedUtc = $startedUtc.ToString('O')
    finishedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    wallMs = $clock.ElapsedMilliseconds
    exitCode = $exitCode
    turns = $calls.Count
    toolCalls = @($calls)
    finalMessage = $finalMessage
    failure = $failure
    failureKind = $failureKind
    failureTool = if ($failure) { $toolName } else { $null }
    mcpTimeoutMs = $mcpTimeoutMs
    serverStderr = $serverStderr
    inputTokens = $null
    outputTokens = $null
    costUsd = $null
}
$hostResult | ConvertTo-Json -Depth 60 | Set-Content -LiteralPath $manifest.hostResultPath -Encoding utf8
if ($exitCode -ne 0) { exit $exitCode }
