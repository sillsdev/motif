[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Worktree', 'Index', 'Tip', 'Range')][string] $Mode,
    [Parameter(Mandatory)][string] $RepositoryRoot,
    [string] $Base,
    [string] $Head
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
    'Worktree' { $arguments += '--worktree' }
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
