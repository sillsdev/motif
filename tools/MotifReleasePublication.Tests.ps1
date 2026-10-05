Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'MotifReleasePublication.psm1') -Force

function Assert-Refused {
    param([scriptblock] $Action, [string] $Reason)
    $failure = $null
    try { & $Action } catch { $failure = $_.Exception.Message }
    if ($null -eq $failure -or -not $failure.Contains($Reason)) {
        throw "Expected refusal containing '$Reason'; got '$failure'."
    }
}

foreach ($version in @('0.2.0-beta001', '0.2.0-beta', '0.2.0-beta.1')) {
    $result = Confirm-MotifReleasePublication $version $version "refs/tags/v$version" $false
    if (-not $result.IsPrerelease) { throw 'Beta releases must be marked prerelease.' }
}
foreach ($version in @('0.2.0', '0.2.0-rc001', '0.2.0-betamax', '0.2.0-BETA001', '0.2.0+beta001')) {
    Assert-Refused { Confirm-MotifReleasePublication $version $version "refs/tags/v$version" $false } 'signing credentials'
}
$stable = Confirm-MotifReleasePublication '0.2.0' '0.2.0' 'refs/tags/v0.2.0' $true
if ($stable.IsPrerelease) { throw 'Stable releases must not be marked prerelease.' }
$rc = Confirm-MotifReleasePublication '0.2.0-rc001' '0.2.0-rc001' 'refs/tags/v0.2.0-rc001' $true
if (-not $rc.IsPrerelease) { throw 'Signed prereleases must be marked prerelease.' }
Assert-Refused { Confirm-MotifReleasePublication '0.2.0-beta001' '0.2.0' 'refs/tags/v0.2.0-beta001' $false } 'must match'
Assert-Refused { Confirm-MotifReleasePublication '0.2.0-beta001' '0.2.0-beta001' 'refs/heads/main' $false } 'on tag'

Write-Host 'Beta publication and stable signing refusal checks passed.' -ForegroundColor Green
