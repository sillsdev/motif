Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Add-ABTranscriptLine([string] $Path, [object] $Event) {
    $line = $Event | ConvertTo-Json -Depth 60 -Compress
    if ($Path -eq '-') { [Console]::Out.WriteLine($line) }
    else { [IO.File]::AppendAllText($Path, $line + [Environment]::NewLine, [Text.UTF8Encoding]::new($false)) }
}

function Receive-ABMcpResponse([object] $Session, [int] $TimeoutMs = 20000) {
    $readTask = $Session.Process.StandardOutput.ReadLineAsync()
    if (-not $readTask.Wait($TimeoutMs)) {
        throw [TimeoutException]::new("MCP server did not answer within $TimeoutMs ms.")
    }
    $line = $readTask.GetAwaiter().GetResult()
    if ($null -eq $line) {
        $stderr = $Session.StderrTask.GetAwaiter().GetResult()
        throw "MCP server closed stdout. $stderr"
    }
    try { return ($line | ConvertFrom-Json -AsHashtable) }
    catch { throw "MCP server wrote a non-JSON line to stdout: $line" }
}

function Send-ABMcpRequest([object] $Session, [string] $Method, [hashtable] $Parameters, [bool] $Notification = $false) {
    $request = [ordered]@{ jsonrpc = '2.0'; method = $Method }
    if (-not $Notification) { $request.id = $Session.NextId; $Session.NextId++ }
    if ($null -ne $Parameters) { $request.params = $Parameters }
    $wire = $request | ConvertTo-Json -Depth 60 -Compress
    $Session.Process.StandardInput.WriteLine($wire)
    $Session.Process.StandardInput.Flush()
    Add-ABTranscriptLine $Session.TranscriptPath ([ordered]@{
        atUtc = [DateTimeOffset]::UtcNow.ToString('O'); source = 'mcp-client'; direction = 'sent'; payload = $request
    })
    if ($Notification) { return $null }
    $timeoutMs = if ($Method -eq 'tools/call') { $Session.ToolTimeoutMs } else { 20000 }
    $response = Receive-ABMcpResponse $Session $timeoutMs
    Add-ABTranscriptLine $Session.TranscriptPath ([ordered]@{
        atUtc = [DateTimeOffset]::UtcNow.ToString('O'); source = 'mcp-client'; direction = 'received'; payload = $response
    })
    if ($response.Contains('error') -and $response.error) {
        throw "MCP error $($response.error.code): $($response.error.message)"
    }
    return $response
}

function New-ABMcpSession(
    [string] $Command,
    [string[]] $Arguments,
    [string] $WorkingDirectory,
    [hashtable] $Environment,
    [string] $TranscriptPath,
    [switch] $SkipToolList,
    [ValidateRange(1, 2147483647)][int] $ToolTimeoutMs = 20000
) {
    $info = [Diagnostics.ProcessStartInfo]::new($Command)
    $info.UseShellExecute = $false
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.CreateNoWindow = $true
    $info.WorkingDirectory = $WorkingDirectory
    foreach ($argument in $Arguments) { [void]$info.ArgumentList.Add([string]$argument) }
    foreach ($key in $Environment.Keys) { $info.Environment[[string]$key] = [string]$Environment[$key] }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    if (-not $process.Start()) { throw "Could not start MCP server: $Command" }
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $session = [pscustomobject]@{ Process = $process; NextId = 1; TranscriptPath = $TranscriptPath; StderrTask = $stderrTask; ToolTimeoutMs = $ToolTimeoutMs }
    try {
        $init = Send-ABMcpRequest $session 'initialize' @{
            protocolVersion = '2024-11-05'
            capabilities = @{}
            clientInfo = @{ name = 'motif-ab-harness'; version = '0.1.0' }
        }
        if (-not $init.result.capabilities.tools) { throw 'MCP server did not advertise tools.' }
        [void](Send-ABMcpRequest $session 'notifications/initialized' @{} $true)
        if ($SkipToolList) {
            $session | Add-Member -NotePropertyName Tools -NotePropertyValue @()
            return $session
        }
        $list = Send-ABMcpRequest $session 'tools/list' $null
        if ($null -eq $list.result.tools) { throw 'MCP server returned no tools list.' }
        $session | Add-Member -NotePropertyName Tools -NotePropertyValue @($list.result.tools)
        return $session
    } catch {
        [void](Close-ABMcpSession $session)
        throw
    }
}

function Invoke-ABMcpTool([object] $Session, [string] $Name, [hashtable] $Arguments) {
    return Send-ABMcpRequest $Session 'tools/call' @{ name = $Name; arguments = $Arguments }
}

function Close-ABMcpSession([object] $Session) {
    if (-not $Session -or $Session.Process.HasExited) { return '' }
    $Session.Process.StandardInput.Close()
    if (-not $Session.Process.WaitForExit(5000)) {
        $Session.Process.Kill($true)
        $Session.Process.WaitForExit()
    }
    return $Session.StderrTask.GetAwaiter().GetResult()
}

Export-ModuleMember -Function Add-ABTranscriptLine, New-ABMcpSession, Invoke-ABMcpTool, Close-ABMcpSession
