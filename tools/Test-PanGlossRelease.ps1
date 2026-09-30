[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $RepositoryRoot,

    [Parameter(Mandatory = $true)]
    [string] $ProbeRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$rids = @('win-x64', 'linux-x64', 'osx-arm64', 'osx-x64')
[System.IO.Directory]::CreateDirectory($ProbeRoot) | Out-Null
$artifactPath = Join-Path $ProbeRoot 'synthetic-pangloss'
[System.IO.File]::WriteAllText($artifactPath, 'synthetic release bytes', [System.Text.UTF8Encoding]::new($false))
$sha256 = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
$assets = [ordered]@{}
foreach ($rid in $rids) {
    $assets[$rid] = [pscustomobject]@{
        url = "https://example.invalid/$rid/pangloss-$rid"
        sha256 = $sha256
    }
}
$pin = [pscustomobject]@{ version = '0.0.0'; tag = 'v0.0.0'; assets = $assets }
[System.IO.File]::WriteAllText(
    (Join-Path $ProbeRoot 'pangloss-release.json'),
    (ConvertTo-Json -InputObject $pin -Depth 5),
    [System.Text.UTF8Encoding]::new($false))

Import-Module (Join-Path $PSScriptRoot 'PanGlossRelease.psm1') -Force
$isUnixHost = -not [OperatingSystem]::IsWindows()
$originalMode = [System.IO.UnixFileMode]::UserRead -bor
    [System.IO.UnixFileMode]::GroupRead -bor [System.IO.UnixFileMode]::OtherRead
foreach ($rid in $rids) {
    if ($isUnixHost) {
        [System.IO.File]::SetUnixFileMode($artifactPath, $originalMode)
    }
    $verified = Get-PinnedPanGlossArtifact -RepositoryRoot $ProbeRoot -RuntimeIdentifier $rid -ArtifactPath $artifactPath
    if ($verified.RuntimeIdentifier -ne $rid) {
        throw "PanGloss pin helper returned $($verified.RuntimeIdentifier) for $rid."
    }
    if ($isUnixHost) {
        $expectedMode = $originalMode
        if (-not $rid.StartsWith('win-', [System.StringComparison]::Ordinal)) {
            $expectedMode = $expectedMode -bor [System.IO.UnixFileMode]::UserExecute
        }
        $actualMode = [System.IO.File]::GetUnixFileMode($artifactPath)
        if ($actualMode -ne $expectedMode) {
            throw "PanGloss pin helper changed $rid mode to $actualMode; expected $expectedMode."
        }
    }
}
Write-Output "PanGloss pin helper covered $($rids.Count) supported RIDs on this host."
