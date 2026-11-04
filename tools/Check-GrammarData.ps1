[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Worktree', 'Index', 'Tip', 'Range')][string] $Mode,
    [Parameter(Mandatory)][string] $RepositoryRoot,
    [string] $Base,
    [string] $Head,
    [string] $Since,
    [switch] $Changed
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath($RepositoryRoot)
$checker = Join-Path $repository 'tools/GrammarDataHygiene/grammar-data-hygiene.cs'
if (-not (Test-Path -LiteralPath $checker -PathType Leaf)) {
    Write-Error "Grammar-data checker is missing from $repository."
    exit 2
}

Import-Module (Join-Path $repository 'tools/MotifToolEnvironment.psm1') -Force
Initialize-MotifToolEnvironment -RepoRoot $repository
$arguments = @()
switch ($Mode) {
    'Worktree' {
        $arguments += '--worktree'
        # -Changed: since the merge-base with origin/main, unless CI or a missing origin/main says to scan everything.
        if ($Changed -and [string]::IsNullOrWhiteSpace($Since) -and $env:CI -notin @('true', '1', 'yes')) {
            $global:LASTEXITCODE = 0
            $mergeBase = ([string] (& git -C $repository merge-base HEAD origin/main 2>$null)).Trim()
            if ($LASTEXITCODE -eq 0 -and $mergeBase -match '^[0-9a-f]{40,64}$') { $Since = $mergeBase }
            else { Write-Host 'No merge-base with origin/main: scanning every tracked file.' }
        }
        # Without -Since every tracked file is read: the safe default, and what CI and the scheduled run use.
        if (-not [string]::IsNullOrWhiteSpace($Since)) { $arguments += @('--since', $Since) }
    }
    'Index' { $arguments += '--index' }
    'Tip' {
        if ([string]::IsNullOrWhiteSpace($Head)) { Write-Error 'Tip mode requires -Head.'; exit 2 }
        $arguments += @('--tip', $Head)
    }
    'Range' {
        if ([string]::IsNullOrWhiteSpace($Base) -or [string]::IsNullOrWhiteSpace($Head)) { Write-Error 'Range mode requires -Base and -Head.'; exit 2 }
        $arguments += @('--range', $Base, $Head)
    }
}
$arguments += @('--repo', $repository)
Push-Location $repository
try {
    & dotnet run --file $checker --artifacts-path (Get-MotifToolArtifactsPath $repository 'grammar-data-hygiene') -- @arguments
    $result = $LASTEXITCODE
}
finally { Pop-Location }
exit $result
