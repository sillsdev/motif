[CmdletBinding()]
param([string] $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')))

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($RepositoryRoot)

function Invoke-Git([string[]] $GitArguments, [switch] $AllowFailure) {
    $output = @(& git -C $root @GitArguments 2>&1)
    $code = $LASTEXITCODE
    if ($code -ne 0 -and -not $AllowFailure) { throw "git $($GitArguments -join ' ') failed: $($output -join ' ')" }
    [pscustomobject]@{ ExitCode = $code; Output = ($output -join "`n").Trim() }
}

$actualRoot = (Invoke-Git @('rev-parse', '--show-toplevel')).Output
if ([IO.Path]::GetFullPath($actualRoot) -ne $root) { throw "RepositoryRoot must name the current worktree root: $actualRoot" }
$gitDirectory = [IO.Path]::GetFullPath((Invoke-Git @('rev-parse', '--absolute-git-dir')).Output)
$adapter = Join-Path $gitDirectory 'motif-grammar-data-hooks'
$configPath = (Invoke-Git @('config', '--path', '--get', 'core.hooksPath') -AllowFailure)
$extension = (Invoke-Git @('config', '--local', '--bool', '--get', 'extensions.worktreeConfig') -AllowFailure)
$commonBare = (Invoke-Git @('config', '--local', '--bool', '--get', 'core.bare') -AllowFailure)
$commonWorktree = (Invoke-Git @('config', '--local', '--get', 'core.worktree') -AllowFailure)

$alreadyInstalled = $false
$originalHooks = $null
if ($configPath.ExitCode -eq 0) {
    $effective = $configPath.Output
    if (-not [IO.Path]::IsPathRooted($effective)) { $effective = Join-Path $root $effective }
    if ([IO.Path]::GetFullPath($effective) -eq [IO.Path]::GetFullPath($adapter)) {
        $alreadyInstalled = $true
        $marker = Join-Path $adapter 'original-hooks-path.txt'
        if (Test-Path -LiteralPath $marker) { $originalHooks = (Get-Content -LiteralPath $marker -Raw).TrimEnd("`r", "`n") }
        if ([string]::IsNullOrWhiteSpace($originalHooks)) { throw 'Installed adapter has no original-hooks-path.txt; refusing to chain recursively.' }
    }
    elseif (-not [string]::IsNullOrWhiteSpace($effective)) { $originalHooks = [IO.Path]::GetFullPath($effective) }
}
if (-not $alreadyInstalled -and $null -eq $originalHooks) {
    $originalHooks = [IO.Path]::GetFullPath((Invoke-Git @('rev-parse', '--git-path', 'hooks')).Output)
}

if ($extension.Output -ne 'true') {
    if ($commonWorktree.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($commonWorktree.Output)) {
        throw "Cannot enable worktree-specific hook configuration while common core.worktree is set to '$($commonWorktree.Output)'; relocate it deliberately first."
    }
    if ($commonBare.ExitCode -eq 0 -and $commonBare.Output -eq 'true') {
        throw 'Cannot enable worktree-specific hook configuration while common core.bare=true; relocate it deliberately first.'
    }
    if ($commonBare.ExitCode -eq 0 -and $commonBare.Output -ne 'false') {
        throw "Cannot enable worktree-specific hook configuration with unsupported common core.bare='$($commonBare.Output)'."
    }
    Invoke-Git @('config', '--local', 'extensions.worktreeConfig', 'true') | Out-Null
}

New-Item -ItemType Directory -Force -Path $adapter | Out-Null
Set-Content -LiteralPath (Join-Path $adapter 'original-hooks-path.txt') -Value $originalHooks -NoNewline -Encoding utf8
$shellQuote = {
    param([string] $Value)
    $replacement = [string]::Concat("'", '"', "'", '"', "'")
    "'" + $Value.Replace("'", $replacement) + "'"
}
$toHookShellPath = {
    param([string] $Value)
    if (-not $IsWindows) { return $Value }
    $gitPath = (Get-Command git -ErrorAction Stop).Source
    $gitRoot = Split-Path (Split-Path $gitPath -Parent) -Parent
    $cygpath = Join-Path $gitRoot 'usr/bin/cygpath.exe'
    if (-not (Test-Path -LiteralPath $cygpath -PathType Leaf)) { throw "Git for Windows path converter is missing: $cygpath" }
    $converted = @(& $cygpath -u $Value 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Could not convert hook path for Git Bash: $Value" }
    ($converted -join '').Trim()
}
$hookFiles = @()
function Test-PosixExecutable([string] $Path) {
    $output = @(& sh -c 'test -x "$1"' 'motif-hook-executable-check' $Path 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -eq 0) { return $true }
    if ($exitCode -eq 1) { return $false }
    throw "Could not determine executable access for hook '$Path' (sh exit $exitCode): $(@($output) -join ' ')"
}

if (Test-Path -LiteralPath $originalHooks -PathType Container) {
    foreach ($file in Get-ChildItem -LiteralPath $originalHooks -File) {
        if ($file.Name.EndsWith('.sample', [StringComparison]::OrdinalIgnoreCase)) { continue }
        $active = $true
        if (-not $IsWindows) {
            $active = Test-PosixExecutable $file.FullName
        }
        if ($active) { $hookFiles += $file }
    }
}

$versionedHook = Join-Path $root 'tools/hooks/pre-commit'
foreach ($file in $hookFiles) {
    if ($file.Name -eq 'pre-commit') { continue }
    $original = & $shellQuote (& $toHookShellPath $file.FullName)
    $body = "#!/bin/sh`nexec $original `"`$@`"`n"
    Set-Content -LiteralPath (Join-Path $adapter $file.Name) -Value $body -NoNewline -Encoding utf8
}
$preCommitFile = $hookFiles | Where-Object Name -eq 'pre-commit' | Select-Object -First 1
$preCommit = if ($preCommitFile) { $preCommitFile.FullName } else { $null }
$parts = @('#!/bin/sh', 'set -eu')
if ($preCommit) {
    $original = & $shellQuote (& $toHookShellPath $preCommit)
    $parts += "if [ -f $original ]; then $original `"`$@`" || exit `$?; else echo 'original pre-commit hook disappeared' >&2; exit 2; fi"
}
$hook = & $shellQuote (& $toHookShellPath $versionedHook)
$parts += "exec sh $hook `"`$@`""
Set-Content -LiteralPath (Join-Path $adapter 'pre-commit') -Value (($parts -join "`n") + "`n") -NoNewline -Encoding utf8
if (-not $IsWindows) {
    Get-ChildItem -LiteralPath $adapter -File | ForEach-Object { [IO.File]::SetUnixFileMode($_.FullName, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::GroupRead -bor [IO.UnixFileMode]::OtherRead -bor [IO.UnixFileMode]::UserExecute) }
}
Invoke-Git @('config', '--worktree', 'core.hooksPath', $adapter) | Out-Null
Write-Output "Installed worktree-local grammar-data hook adapter at $adapter (original hooks: $originalHooks)."
