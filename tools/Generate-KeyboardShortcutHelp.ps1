param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$executableName = if ($IsWindows) { 'motif.exe' } else { 'motif' }
$executable = Join-Path $repositoryRoot "bin/$Configuration/$executableName"
$export = & $executable help --all --json
if ($LASTEXITCODE -ne 0) {
    throw "The Motif Help catalog export failed with exit code $LASTEXITCODE."
}

$catalog = ($export -join "`n") | ConvertFrom-Json
$entry = $catalog.entries | Where-Object { $_.kind -eq 'guide' -and $_.code -eq 'keyboard-shortcuts' }
if ($null -eq $entry -or [string]::IsNullOrWhiteSpace($entry.helpPage)) {
    throw 'The Help catalog did not render guide:keyboard-shortcuts.'
}

$pagePath = Join-Path $repositoryRoot 'src/SIL.Motif.Help/Content/en/guide/keyboard-shortcuts.md'
[System.IO.File]::WriteAllText($pagePath, $entry.helpPage, [System.Text.UTF8Encoding]::new($false))
