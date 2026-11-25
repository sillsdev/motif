# One implementation of "run Help in off mode", shared by the build gate and the golden updater.

Set-StrictMode -Version Latest

# The golden is a folder: `_envelope.json` holds every top-level field except `entries`, and each Help entry is its own
# file, `<kind>/<code>.json` (a space in a code becomes `_`, a slash `~`), so a change to one command touches one file.
function Get-MotifHelpGoldenPath {
    param([Parameter(Mandatory)][string] $RepoRoot)
    Join-Path $RepoRoot 'tests/SIL.Motif.Tests.Cli/Golden/help'
}

# Runs `motif help --all --json` with developer commands, Autonomous and Advanced AI mode off.
function Invoke-MotifOffModeHelp {
    param(
        [Parameter(Mandatory)][string] $RepoRoot,
        [Parameter(Mandatory)][string] $Configuration
    )
    $cli = Join-Path $RepoRoot "bin/$Configuration/$(if ($IsWindows) { 'motif.exe' } else { 'motif' })"
    if (-not (Test-Path -LiteralPath $cli)) { throw "The CLI was not built at $cli." }
    $scratch = Join-Path ([IO.Path]::GetTempPath()) ('motif-help-golden-' + [Guid]::NewGuid().ToString('N'))
    [void] [IO.Directory]::CreateDirectory($scratch)
    [IO.File]::WriteAllText((Join-Path $scratch 'advanced-ai-mode.json'), '{"advancedAiMode":false}')
    $start = [Diagnostics.ProcessStartInfo]::new($cli)
    foreach ($argument in 'help', '--all', '--json') { $start.ArgumentList.Add($argument) }
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.UseShellExecute = $false
    $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    [void] $start.Environment.Remove('MOTIF_DEVELOPER_COMMANDS')
    [void] $start.Environment.Remove('MOTIF_AUTONOMOUS')
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

# Splits `help --all --json` output into the golden's files: a map of relative path (forward slashes) to the exact
# text of that file. Parsed with System.Text.Json rather than ConvertFrom-Json -AsHashtable, which throws on object keys
# that differ only in case; the map is ordinal (case-sensitive), and a path that collides when case is ignored is
# rejected because the golden must check out on a case-insensitive file system.
function Split-MotifHelpExport {
    param([Parameter(Mandatory)][string] $Json)
    $files = [Collections.Generic.SortedDictionary[string, string]]::new([StringComparer]::Ordinal)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $document = [Text.Json.JsonDocument]::Parse($Json)
    try {
        $envelope = [Collections.Generic.List[string]]::new()
        foreach ($property in $document.RootElement.EnumerateObject()) {
            if ($property.Name -ceq 'entries') { continue }
            $envelope.Add('  ' + '"' + [Text.Json.JsonEncodedText]::Encode($property.Name).ToString() + '"' + ': ' + $property.Value.GetRawText())
        }
        $files['_envelope.json'] = "{`n" + ($envelope -join ",`n") + "`n}`n"
        foreach ($entry in $document.RootElement.GetProperty('entries').EnumerateArray()) {
            $kind = $entry.GetProperty('kind').GetString()
            $code = $entry.GetProperty('code').GetString()
            $path = "$kind/$($code.Replace(' ', '_').Replace('/', '~')).json"
            if (-not $seen.Add($path)) { throw "The Help golden lists '$kind $code' twice, or two entries share the file '$path' ignoring case." }
            # The entry sits at four spaces in the export; the file holds it at the left margin.
            $files[$path] = ($entry.GetRawText() -replace '(?m)^    ', '') + "`n"
        }
    }
    finally { $document.Dispose() }
    $files
}

# Reads the golden folder into the same map Split-MotifHelpExport returns.
function Read-MotifHelpGolden {
    param([Parameter(Mandatory)][string] $RepoRoot)
    $root = Get-MotifHelpGoldenPath $RepoRoot
    $files = [Collections.Generic.SortedDictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
        $files[$relative] = [IO.File]::ReadAllText($file.FullName).Replace("`r`n", "`n")
    }
    $files
}

# Replaces the golden folder with the files of the given export.
function Write-MotifHelpGolden {
    param([Parameter(Mandatory)][string] $RepoRoot, [Parameter(Mandatory)][string] $Json)
    $root = Get-MotifHelpGoldenPath $RepoRoot
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
    $files = Split-MotifHelpExport $Json
    foreach ($path in $files.Keys) {
        $target = Join-Path $root $path
        [void] [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
        [IO.File]::WriteAllText($target, $files[$path], [Text.UTF8Encoding]::new($false))
    }
    $files.Count
}

# Returns the golden files whose content differs from the built CLI's Help (a file only on one side counts), or $null
# when they match.
function Compare-MotifHelpGolden {
    param(
        [Parameter(Mandatory)][string] $RepoRoot,
        [Parameter(Mandatory)][string] $Configuration
    )
    $after = Split-MotifHelpExport (Invoke-MotifOffModeHelp -RepoRoot $RepoRoot -Configuration $Configuration)
    $before = Read-MotifHelpGolden $RepoRoot
    $differ = @($before.Keys + $after.Keys | Sort-Object -Unique -CaseSensitive | Where-Object { $before[$_] -cne $after[$_] })
    if ($differ.Count -eq 0) { return $null }
    $differ
}

Export-ModuleMember -Function Get-MotifHelpGoldenPath, Invoke-MotifOffModeHelp, Split-MotifHelpExport, Read-MotifHelpGolden, Write-MotifHelpGolden, Compare-MotifHelpGolden
