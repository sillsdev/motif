[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $RawRoot,
    [Parameter(Mandatory = $true)][string] $SelectionManifest,
    [string] $OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$rawRoot = [IO.Path]::GetFullPath($RawRoot)
$selectionPath = [IO.Path]::GetFullPath($SelectionManifest)
$cacheRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'bin/.cache/research-samples'))
if (-not $OutputRoot) { $OutputRoot = $cacheRoot }
$outputRoot = [IO.Path]::GetFullPath($OutputRoot)
$pathComparison = if ([OperatingSystem]::IsWindows()) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
$selectionRelative = [IO.Path]::GetRelativePath($repositoryRoot, $selectionPath)
if (-not [IO.Path]::IsPathRooted($selectionRelative) -and $selectionRelative -ne '..' -and
    -not $selectionRelative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, $pathComparison) -and
    -not $selectionRelative.StartsWith((Join-Path 'bin/.cache' ''), $pathComparison)) {
    throw 'Selection manifests stored in the checkout must be in its ignored bin/.cache directory.'
}
$outputRelative = [IO.Path]::GetRelativePath($cacheRoot, $outputRoot)
if ([IO.Path]::IsPathRooted($outputRelative) -or $outputRelative -eq '..' -or
    $outputRelative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, $pathComparison)) {
    throw 'Research extraction output must remain under bin/.cache/research-samples.'
}
$manifest = Get-Content -LiteralPath $selectionPath -Raw | ConvertFrom-Json
if (-not $manifest.datasets -or $manifest.datasets.Count -eq 0) { throw 'Selection manifest has no datasets.' }

foreach ($dataset in $manifest.datasets) {
    $sourcePath = [IO.Path]::GetFullPath((Join-Path $rawRoot ([string] $dataset.input)))
    $sourcePrefix = $rawRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $sourcePath.StartsWith($sourcePrefix, $pathComparison)) { throw 'Raw input escapes RawRoot.' }
    $destinationPath = [IO.Path]::GetFullPath((Join-Path $outputRoot ([string] $dataset.output)))
    $outputPrefix = $outputRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $destinationPath.StartsWith($outputPrefix, $pathComparison)) { throw 'Output escapes OutputRoot.' }
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Raw input is missing: $sourcePath" }
    $keys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($key in $dataset.keys) { [void] $keys.Add([string] $key) }
    if ($keys.Count -eq 0) { throw "Dataset selection has no keys: $($dataset.output)" }
    $selected = [System.Collections.Generic.List[string]]::new()
    foreach ($line in [IO.File]::ReadLines($sourcePath)) {
        $columns = $line.Split("`t")
        if ($dataset.keyColumn -lt 0 -or $dataset.keyColumn -ge $columns.Length) { continue }
        if ($keys.Contains($columns[[int] $dataset.keyColumn])) { $selected.Add($line) }
    }
    if ($selected.Count -eq 0) { throw "No source rows matched the selection for $($dataset.output)." }
    [IO.Directory]::CreateDirectory((Split-Path $destinationPath -Parent)) | Out-Null
    [IO.File]::WriteAllLines($destinationPath, $selected, [Text.UTF8Encoding]::new($false))
}
