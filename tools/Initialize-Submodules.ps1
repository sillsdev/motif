# Fills external/SIL.TextEditing when a fresh clone or worktree left it empty, so restore finds its projects.
param([Parameter(Mandatory)][string] $RepoRoot)

$marker = Join-Path $RepoRoot 'external/SIL.TextEditing/src/SIL.TextEditing/SIL.TextEditing.csproj'
if (Test-Path -LiteralPath $marker -PathType Leaf) { return }
Write-Host '==> git submodule update --init external/SIL.TextEditing' -ForegroundColor Cyan
& git -C $RepoRoot -c gc.auto=0 submodule update --init external/SIL.TextEditing
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $marker -PathType Leaf)) {
    throw 'external/SIL.TextEditing is missing. Run: git submodule update --init external/SIL.TextEditing'
}
