[CmdletBinding()]
param(
    [switch] $GitHubEnvironment
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
[xml] $props = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$versions = @($props.SelectNodes('/Project/PropertyGroup/VersionPrefix'))
if ($versions.Count -ne 1 -or $versions[0].InnerText -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw 'Directory.Build.props must declare exactly one stable three-part VersionPrefix.'
}
$version = [Version]::Parse($versions[0].InnerText)
$result = [pscustomobject]@{
    ProductVersion = $version.ToString(3)
    NextProductVersion = [Version]::new($version.Major, $version.Minor, $version.Build + 1).ToString(3)
}
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
