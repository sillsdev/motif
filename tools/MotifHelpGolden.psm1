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

# Returns the entry codes whose Help differs from the golden, or $null when the files match.
function Compare-MotifHelpGolden {
    param(
        [Parameter(Mandatory)][string] $RepoRoot,
        [Parameter(Mandatory)][string] $Configuration
    )
    $actual = Invoke-MotifOffModeHelp -RepoRoot $RepoRoot -Configuration $Configuration
    $expected = [IO.File]::ReadAllText((Get-MotifHelpGoldenPath $RepoRoot)).Replace("`r`n", "`n")
    if ($actual -ceq $expected) { return $null }
    $before = @{}
    foreach ($entry in (ConvertFrom-Json $expected -AsHashtable).entries) { $before["$($entry.kind):$($entry.code)"] = ConvertTo-Json $entry -Depth 20 -Compress }
    $after = @{}
    foreach ($entry in (ConvertFrom-Json $actual -AsHashtable).entries) { $after["$($entry.kind):$($entry.code)"] = ConvertTo-Json $entry -Depth 20 -Compress }
    $differ = @($before.Keys + $after.Keys | Sort-Object -Unique | Where-Object { $before[$_] -cne $after[$_] })
    if ($differ.Count -eq 0) { $differ = @('(top-level fields)') }
    $differ
}

Export-ModuleMember -Function Get-MotifHelpGoldenPath, Invoke-MotifOffModeHelp, Compare-MotifHelpGolden
