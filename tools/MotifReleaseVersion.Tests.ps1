Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'MotifReleaseVersion.psm1') -Force

function Assert-NextVersion {
    param([string] $Version, [string] $Expected)
    $actual = Get-MotifReleaseVersion -Version $Version
    if ($actual.ProductVersion -cne $Version -or $actual.NextProductVersion -cne $Expected) {
        throw "Unexpected version progression for $Version."
    }
}

function Assert-Refused {
    param([scriptblock] $Action, [string] $Reason)
    $failure = $null
    try { & $Action } catch { $failure = $_.Exception.Message }
    if ($null -eq $failure -or -not $failure.Contains($Reason)) {
        throw "Expected refusal containing '$Reason'; got '$failure'."
    }
}

Assert-NextVersion '0.2.0-beta001' '0.2.0-beta002'
Assert-NextVersion '0.2.0-beta009' '0.2.0-beta010'
Assert-NextVersion '0.2.0-beta.9' '0.2.0-beta.10'
Assert-NextVersion '0.2.0-rc' '0.2.0-rc.1'
Assert-NextVersion '0.2.0' '0.2.1'
Assert-Refused { Get-MotifReleaseVersion '0.2.0-01' } 'SemVer'
Assert-Refused { Get-MotifReleaseVersion '0.2.0-beta999' } 'exhausted'

$source = & (Join-Path $PSScriptRoot 'Get-ProductVersion.ps1')
$envFile = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString() + '.env')
$previous = $env:GITHUB_ENV
try {
    $env:GITHUB_ENV = $envFile
    & (Join-Path $PSScriptRoot 'Get-ProductVersion.ps1') -GitHubEnvironment
    $exported = @(Get-Content -LiteralPath $envFile)
    if ($exported.Count -ne 2 -or $exported[0] -cne "PRODUCT_VERSION=$($source.ProductVersion)" -or
        $exported[1] -cne "NEXT_PRODUCT_VERSION=$($source.NextProductVersion)") {
        throw 'The workflow environment must receive the exact product and smoke versions.'
    }
}
finally {
    $env:GITHUB_ENV = $previous
    Remove-Item -LiteralPath $envFile -ErrorAction SilentlyContinue
}
Write-Host 'Release version progression and environment export checks passed.' -ForegroundColor Green
