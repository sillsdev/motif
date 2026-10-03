[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $StageDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$stage = [System.IO.Path]::GetFullPath($StageDirectory)
if (-not (Test-Path -LiteralPath $stage -PathType Container)) {
    throw "Package staging directory does not exist: $stage"
}

$manifestPath = Join-Path $stage 'release-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Package manifest is missing: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$records = [System.Collections.Generic.List[object]]::new()
$hashesByPath = [System.Collections.Generic.Dictionary[string, string]]::new(
    [System.StringComparer]::Ordinal)
$payloadFiles = @(Get-ChildItem -LiteralPath $stage -File -Recurse |
    Where-Object { [System.IO.Path]::GetFullPath($_.FullName) -ne $manifestPath } |
    Sort-Object FullName)

foreach ($file in $payloadFiles) {
    $relativePath = [System.IO.Path]::GetRelativePath($stage, $file.FullName).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $records.Add([ordered]@{
        path = $relativePath
        size = $file.Length
        sha256 = $hash
    })
    $hashesByPath.Add($relativePath, $hash)
}

$manifest.files = @($records)
foreach ($dependency in @($manifest.dependencies)) {
    if ($dependency.name -eq 'PanGloss') {
        $parserPath = [string] $dependency.path
        if (-not $hashesByPath.ContainsKey($parserPath)) {
            throw "Package manifest PanGloss file is missing: $parserPath"
        }
        $dependency.sha256 = $hashesByPath[$parserPath]
    }
    elseif ($dependency.name -eq 'SIL ICU') {
        foreach ($file in @($dependency.files)) {
            $icuPath = [string] $file.path
            if (-not $hashesByPath.ContainsKey($icuPath)) {
                throw "Package manifest SIL ICU file is missing: $icuPath"
            }
            $file.sha256 = $hashesByPath[$icuPath]
        }
    }
}

$manifestJson = $manifest | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText(
    $manifestPath,
    $manifestJson + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

& (Join-Path $PSScriptRoot 'test-package-stage.ps1') `
    -StageDirectory $stage `
    -RuntimeIdentifier ([string] $manifest.runtimeIdentifier)
