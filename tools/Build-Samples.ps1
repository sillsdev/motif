[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$Sample
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$samplesRoot = Join-Path $repositoryRoot 'samples'
$sampleOutputRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "bin\$Configuration\samples"))
$builderPath = Join-Path $repositoryRoot "bin\$Configuration\SIL.Motif.SampleProjects.exe"
$sampleSpecs = @(Get-ChildItem -LiteralPath $samplesRoot -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'sample.json') })

if ($Sample) {
    $sampleSpecs = @($sampleSpecs | Where-Object Name -eq $Sample)
    if ($sampleSpecs.Count -eq 0) {
        throw "No sample with id '$Sample' exists under '$samplesRoot'."
    }
}

if (-not (Test-Path -LiteralPath $builderPath -PathType Leaf)) {
    throw "Sample builder not found at '$builderPath'. Run ./build.ps1 first."
}

$sampleOutputPrefix = $sampleOutputRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
foreach ($sampleFolder in $sampleSpecs) {
    $sampleId = $sampleFolder.Name
    $sampleOutput = [System.IO.Path]::GetFullPath((Join-Path $sampleOutputRoot $sampleId))
    if (-not $sampleOutput.StartsWith($sampleOutputPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Sample output path '$sampleOutput' is outside '$sampleOutputRoot'."
    }

    if (Test-Path -LiteralPath $sampleOutput) {
        Remove-Item -LiteralPath $sampleOutput -Recurse -Force
    }
    New-Item -ItemType Directory -Path $sampleOutput -Force | Out-Null

    $specPath = Join-Path $sampleFolder.FullName 'sample.json'
    $bugsPath = Join-Path $sampleFolder.FullName 'bugs.json'
    $fixedRoot = Join-Path $sampleOutput 'fixed'
    $fixedJson = & $builderPath 'build' $specPath $fixedRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Builder failed for fixed sample '$sampleId' with exit code $LASTEXITCODE."
    }
    $fixed = $fixedJson | ConvertFrom-Json
    Move-Item -LiteralPath $fixed.backupPath -Destination (Join-Path $sampleOutputRoot "$sampleId-fixed.fwbackup") -Force

    if (Test-Path -LiteralPath $bugsPath -PathType Leaf) {
        $bugIds = @((Get-Content -LiteralPath $bugsPath -Raw | ConvertFrom-Json).id)
        if ($bugIds.Count -gt 0) {
            $brokenRoot = Join-Path $sampleOutput 'broken'
            $arguments = @('build', $specPath, $brokenRoot, '--bugs', $bugsPath)
            foreach ($bugId in $bugIds) {
                $arguments += @('--bug', $bugId)
            }
            $brokenJson = & $builderPath @arguments
            if ($LASTEXITCODE -ne 0) {
                throw "Builder failed for broken sample '$sampleId' with exit code $LASTEXITCODE."
            }
            $broken = $brokenJson | ConvertFrom-Json
            Move-Item -LiteralPath $broken.backupPath -Destination (Join-Path $sampleOutputRoot "$sampleId-broken.fwbackup") -Force
        }
    }
}
