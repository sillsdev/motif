Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'MotifRestore.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'MotifTestSlot.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'MotifTestRunPlan.psm1') -Force

$names = 'CI', 'MOTIF_BUILD_NODES', 'MOTIF_TEST_SLOTS', 'MOTIF_TEST_THREADS', 'MOTIF_TEST_PROCESSES', 'MOTIF_TEST_SLOT_DIR', 'TMPDIR', 'TMP', 'TEMP'
$saved = @{}
$realTempPath = [IO.Path]::GetTempPath()
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

    Set-Environment @{}
    Assert-Equal (Get-MotifTestProcessConcurrency -ProcessorCount 20 -MemoryBytes 32GB) 12 'processes for 0.6 of 20'
    Assert-Equal (Get-MotifTestProcessConcurrency -ProcessorCount 4 -MemoryBytes 32GB) 2 'process floor'
    Assert-Equal (Get-MotifTestProcessConcurrency -ProcessorCount 20 -MemoryBytes 8GB) 4 'processes bounded by memory'
    Assert-Equal (Get-MotifTestProcessConcurrency -ProcessorCount 64 -MemoryBytes 256GB) 12 'process ceiling'
    Set-Environment @{ CI = 'true' }
    Assert-Equal (Get-MotifTestProcessConcurrency -ProcessorCount 4 -MemoryBytes 16GB) 3 'CI processes on 4 processors'
    Assert-Equal (Get-MotifTestProcessConcurrency -ProcessorCount 4 -MemoryBytes 4GB) 2 'CI processes bounded by memory'
    Assert-Equal (Get-MotifTestProcessConcurrency -ProcessorCount 2 -MemoryBytes 16GB) 2 'CI process floor'
    Assert-Equal (Get-MotifTestProcessConcurrency -ProcessorCount 64 -MemoryBytes 256GB) 8 'CI process ceiling'
    Set-Environment @{}
    Set-Environment @{ MOTIF_TEST_PROCESSES = '3' }
    Assert-Equal (Get-MotifTestProcessConcurrency -ProcessorCount 20) 3 'explicit processes'
    Set-Environment @{ MOTIF_TEST_PROCESSES = 'x' }
    Assert-Equal (Get-MotifTestProcessConcurrency -ProcessorCount 20 -MemoryBytes 32GB) 12 'bad processes fall back'
    Set-Environment @{}
    Assert-Equal (Get-MotifTestThreadCount -ProjectConcurrency 3 -ProcessorCount 20) 6 'threads for a third of 20'
    Assert-Equal (Get-MotifTestThreadCount -ProjectConcurrency 12 -ProcessorCount 20) 2 'thread floor'
    Assert-Equal (Get-MotifTestThreadCount -ProjectConcurrency 0 -ProcessorCount 20) 20 'zero concurrency is one'
    Set-Environment @{ MOTIF_TEST_THREADS = '1' }
    Assert-Equal (Get-MotifTestThreadCount -ProjectConcurrency 3 -ProcessorCount 20) 1 'explicit threads'
    Set-Environment @{ MOTIF_TEST_THREADS = 'x' }
    Assert-Equal (Get-MotifTestThreadCount -ProjectConcurrency 3 -ProcessorCount 20) 6 'bad threads fall back'

    Set-Environment @{ CI = 'true' }
    Assert-Equal (Get-MotifTestThreadCount -ProjectConcurrency 3 -ProcessorCount 20) 0 'CI leaves xUnit threads alone'
    Set-Environment @{ CI = 'yes'; MOTIF_TEST_THREADS = '2' }
    Assert-Equal (Get-MotifTestThreadCount -ProjectConcurrency 3 -ProcessorCount 20) 2 'explicit threads beat CI'

    Set-Environment @{}
    if ($IsWindows) {
        Assert-Equal (Get-MotifTestSlotDirectory -TempPath 'C:\t') 'C:\t\motif-test-slots' 'Windows slot folder follows the TEMP given'
    }
    else {
        $plain = Get-MotifTestSlotDirectory
        Set-Environment @{ TMPDIR = '/private/agent-tmp'; TMP = '/private/agent-tmp'; TEMP = '/private/agent-tmp' }
        Assert-Equal (Get-MotifTestSlotDirectory) $plain 'a relocated TMPDIR does not move the slot folder'
        Assert-Equal $plain '/tmp/motif-test-slots' 'default slot folder'
    }
    Set-Environment @{ MOTIF_TEST_SLOT_DIR = '/shared/slots' }
    Assert-Equal (Get-MotifTestSlotDirectory) '/shared/slots' 'MOTIF_TEST_SLOT_DIR wins'

    $blocker = Join-Path $realTempPath "motif-slot-blocker-$([guid]::NewGuid().ToString('N'))"
    [IO.File]::WriteAllText($blocker, 'x')
    try {
        Set-Environment @{ MOTIF_TEST_SLOTS = '2' }
        $slot = Enter-MotifTestSlot -Directory (Join-Path $blocker 'slots') -WarningAction SilentlyContinue
        Assert-Equal ($null -eq $slot) $true 'an unusable slot folder means no slot, not a failure'
    }
    finally { Remove-Item -LiteralPath $blocker -Force }
}
finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
}

Write-Host 'Test resource sizing checks passed.' -ForegroundColor Green
