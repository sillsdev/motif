Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'MotifReleaseVersion.psm1') -Force

function Confirm-MotifReleasePublication {
    param(
        [Parameter(Mandatory)][string] $Version,
        [Parameter(Mandatory)][string] $ProductVersion,
        [Parameter(Mandatory)][string] $GitRef,
        [Parameter(Mandatory)][bool] $HasSigningCredentials
    )

    $parts = Get-MotifSemanticVersionParts -Version $Version
    if ($Version -cne $ProductVersion) {
        throw 'release_version must match VersionPrefix in Directory.Build.props.'
    }
    if ($GitRef -cne "refs/tags/v$Version") {
        throw "Dispatch this workflow on tag v$Version to release that version."
    }
    if (-not $HasSigningCredentials -and $parts.Suffix -cnotmatch '^beta(?:[0-9]+)?(?:\.[0-9A-Za-z-]+)*$') {
        throw 'Trusted signing credentials are required unless the release has a -beta suffix.'
    }
    [pscustomobject]@{ IsPrerelease = $parts.Suffix.Length -gt 0 }
}

Export-ModuleMember -Function Confirm-MotifReleasePublication
