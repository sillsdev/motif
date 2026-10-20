# One implementation of "run Help in off mode", shared by the build gate and the golden updater.

Set-StrictMode -Version Latest

function Get-MotifHelpGoldenPath {
    param([Parameter(Mandatory)][string] $RepoRoot)
    Join-Path $RepoRoot 'tests/SIL.Motif.Tests.Cli/Golden/help-all-current.json'
}

# Runs `motif help --all --json` as the golden test does: developer commands off, Advanced AI mode off
# (an absent preference file reads as disabled). Returns the standard output with LF newlines.
function Invoke-MotifOffModeHelp {
    param(
        [Parameter(Mandatory)][string] $RepoRoot,
        [Parameter(Mandatory)][string] $Configuration
    )
    $cli = Join-Path $RepoRoot "bin/$Configuration/$(if ($IsWindows) { 'motif.exe' } else { 'motif' })"
    if (-not (Test-Path -LiteralPath $cli)) { throw "The CLI was not built at $cli." }
    $scratch = Join-Path ([IO.Path]::GetTempPath()) ('motif-help-golden-' + [Guid]::NewGuid().ToString('N'))
    $start = [Diagnostics.ProcessStartInfo]::new($cli)
    foreach ($argument in 'help', '--all', '--json') { $start.ArgumentList.Add($argument) }
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.UseShellExecute = $false
    $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    [void] $start.Environment.Remove('MOTIF_DEVELOPER_COMMANDS')
    $start.Environment['MOTIF_ADVANCED_AI_MODE_PATH'] = Join-Path $scratch 'advanced-ai-mode.json'
    # Every CLI action appends a usage-log line under the worker root (default: per user), even `help`.
    # Redirect it so the gate neither reads nor writes the user's state.
    $start.Environment['MOTIF_WORKER_ROOT'] = Join-Path $scratch 'worker'
    $process = [Diagnostics.Process]::Start($start)
    try {
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) { $process.Kill($true); throw 'motif help did not exit within 30 seconds.' }
        if ($process.ExitCode -ne 0) { throw "motif help exited $($process.ExitCode): $($errorTask.GetAwaiter().GetResult())" }
        $outputTask.GetAwaiter().GetResult().Replace("`r`n", "`n")
    }
    finally {
        $process.Dispose()
        if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
    }
}

# Maps "kind:code" to each entry's raw JSON text. Parsed with System.Text.Json rather than
# ConvertFrom-Json -AsHashtable, which throws on object keys that differ only in case; the map itself is
# ordinal (case-sensitive), so entries whose codes differ only in case stay distinct.
function Get-MotifHelpEntryJson {
    param([Parameter(Mandatory)][string] $Json)
    $map = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    $document = [Text.Json.JsonDocument]::Parse($Json)
    try {
        foreach ($entry in $document.RootElement.GetProperty('entries').EnumerateArray()) {
            $key = "$($entry.GetProperty('kind').GetString()):$($entry.GetProperty('code').GetString())"
            if ($map.ContainsKey($key)) { throw "The Help golden lists '$key' twice." }
            $map[$key] = $entry.GetRawText()
        }
    }
    finally { $document.Dispose() }
    $map
}

# Returns the entry codes whose Help differs from the golden, or $null when the files match.
function Compare-MotifHelpGolden {
    param(
        [Parameter(Mandatory)][string] $RepoRoot,
        [Parameter(Mandatory)][string] $Configuration
    )
    $actual = Invoke-MotifOffModeHelp -RepoRoot $RepoRoot -Configuration $Configuration
    $expected = [IO.File]::ReadAllText((Get-MotifHelpGoldenPath $RepoRoot)).Replace("`r`n", "`n")
    if ($actual -ceq $expected) { return $null }
    $before = Get-MotifHelpEntryJson $expected
    $after = Get-MotifHelpEntryJson $actual
    $differ = @($before.Keys + $after.Keys | Sort-Object -Unique -CaseSensitive | Where-Object { $before[$_] -cne $after[$_] })
    if ($differ.Count -eq 0) { $differ = @('(top-level fields)') }
    $differ
}

Export-ModuleMember -Function Get-MotifHelpGoldenPath, Invoke-MotifOffModeHelp, Compare-MotifHelpGolden
