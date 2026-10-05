[CmdletBinding()]
param(
    [switch] $GitHubEnvironment
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
[xml] $props = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$versions = @($props.SelectNodes('/Project/PropertyGroup/VersionPrefix'))
if ($versions.Count -ne 1) {
    throw 'Directory.Build.props must declare exactly one VersionPrefix.'
}
Import-Module (Join-Path $PSScriptRoot 'MotifReleaseVersion.psm1') -Force
$result = Get-MotifReleaseVersion -Version $versions[0].InnerText
if ($GitHubEnvironment) {
    if ([string]::IsNullOrWhiteSpace($env:GITHUB_ENV)) {
        throw 'GITHUB_ENV is required to export the package versions.'
    }
    [string[]] $lines = @(
        "PRODUCT_VERSION=$($result.ProductVersion)",
        "NEXT_PRODUCT_VERSION=$($result.NextProductVersion)"
    )
    [System.IO.File]::AppendAllLines($env:GITHUB_ENV, $lines, [System.Text.UTF8Encoding]::new($false))
}
else {
    $result
}
