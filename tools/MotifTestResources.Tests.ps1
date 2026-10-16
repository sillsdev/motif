Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'MotifRestore.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'MotifTestSlot.psm1') -Force

$names = 'CI', 'MOTIF_BUILD_NODES', 'MOTIF_TEST_SLOTS'
$saved = @{}
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }

function Assert-Equal {
    param($Actual, $Expected, [string] $What)
    if ($Actual -ne $Expected) { throw "$What was '$Actual', expected '$Expected'." }
}

function Set-Environment {
    param([hashtable] $Values)
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $Values[$name]) }
}

try {
    Set-Environment @{}
    Assert-Equal (Get-MotifBuildNodeLimit -ProcessorCount 20) 5 'developer node cap on 20 processors'
    Assert-Equal (Get-MotifBuildNodeLimit -ProcessorCount 4) 2 'developer node cap floor'
    Set-Environment @{ MOTIF_BUILD_NODES = '3' }
    Assert-Equal (Get-MotifBuildNodeLimit -ProcessorCount 20) 3 'explicit node count'
    Set-Environment @{ MOTIF_BUILD_NODES = '0' }
    Assert-Equal (Get-MotifBuildNodeLimit -ProcessorCount 20) 5 'zero node count falls back'
    foreach ($ci in 'true', 'TRUE', '1', 'yes', 'Yes') {
        Set-Environment @{ CI = $ci }
        Assert-Equal (Get-MotifBuildNodeLimit -ProcessorCount 20) 0 "CI=$ci node cap"
    }
    Set-Environment @{ CI = 'false' }
    Assert-Equal (Get-MotifBuildNodeLimit -ProcessorCount 20) 5 'CI=false node cap'
    Set-Environment @{ CI = 'true'; MOTIF_BUILD_NODES = '4' }
    Assert-Equal (Get-MotifBuildNodeLimit -ProcessorCount 20) 4 'explicit node count beats CI'

    Set-Environment @{}
    Assert-Equal (Get-MotifTestSlotCount -ProcessorCount 20) 4 'developer slots on 20 processors'
    Assert-Equal (Get-MotifTestSlotCount -ProcessorCount 4) 2 'developer slot floor'
    Set-Environment @{ MOTIF_TEST_SLOTS = '' }
    Assert-Equal (Get-MotifTestSlotCount -ProcessorCount 20) 4 'empty slots means the default'
    Set-Environment @{ MOTIF_TEST_SLOTS = '7' }
    Assert-Equal (Get-MotifTestSlotCount -ProcessorCount 20) 7 'explicit slots'
    Set-Environment @{ MOTIF_TEST_SLOTS = '0' }
    Assert-Equal (Get-MotifTestSlotCount -ProcessorCount 20) 0 'slots explicitly off'
    Assert-Equal ($null -eq (Enter-MotifTestSlot)) $true 'no slot taken when off'
    Set-Environment @{ CI = 'true' }
    Assert-Equal (Get-MotifTestSlotCount -ProcessorCount 20) 0 'CI takes no slot'
    Set-Environment @{ CI = 'true'; MOTIF_TEST_SLOTS = '2' }
    Assert-Equal (Get-MotifTestSlotCount -ProcessorCount 20) 2 'CI honours an explicit slot count'
}
finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
}

Write-Host 'Test resource sizing checks passed.' -ForegroundColor Green
