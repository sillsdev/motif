[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $StageDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64', 'osx-x64')]
    [string] $RuntimeIdentifier
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$stage = [System.IO.Path]::GetFullPath($StageDirectory)
if (-not (Test-Path -LiteralPath $stage -PathType Container)) {
    throw "Package staging directory does not exist: $stage"
}

$suffix = if ($RuntimeIdentifier -eq 'win-x64') { '.exe' } else { '' }
$entryPoints = @(
    [ordered]@{ name = 'app'; file = "SIL.Motif.App$suffix" },
    [ordered]@{ name = 'cli'; file = "motif$suffix" },
    [ordered]@{ name = 'worker'; file = "SIL.Motif.Worker$suffix" },
    [ordered]@{ name = 'parser'; file = "pangloss$suffix" }
)

foreach ($entryPoint in $entryPoints) {
    $path = Join-Path $stage $entryPoint.file
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Package entry point '$($entryPoint.name)' is missing from the staging root: $path"
    }
}

foreach ($directoryName in @('app', 'cli')) {
    if (Test-Path -LiteralPath (Join-Path $stage $directoryName)) {
        throw "Package staging must not split entry points into '$directoryName': $stage"
    }
}

$manifestPath = Join-Path $stage 'release-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Package manifest is missing: $manifestPath"
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.runtimeIdentifier -ne $RuntimeIdentifier) {
    throw "Package manifest RID '$($manifest.runtimeIdentifier)' does not match '$RuntimeIdentifier'."
}

foreach ($entryPoint in $entryPoints) {
    $record = @($manifest.entryPoints | Where-Object { $_.name -eq $entryPoint.name })
    if ($record.Count -ne 1 -or $record[0].path -ne $entryPoint.file) {
        throw "Package manifest does not record '$($entryPoint.name)' at the staging root."
    }
}

foreach ($record in $manifest.files) {
    $path = Join-Path $stage ($record.path.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Package manifest lists a missing file: $($record.path)"
    }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $record.sha256) {
        throw "Package manifest hash does not match: $($record.path)"
    }
}

Write-Host "Package staging is complete for ${RuntimeIdentifier}: $stage" -ForegroundColor Green
