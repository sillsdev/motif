[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$fixture = [IO.Directory]::CreateTempSubdirectory('motif-roundtrip-wrapper-test-')
$oldOutcome = $env:MOTIF_TEST_ROUND_TRIP_OUTCOME
try {
    $tools = Join-Path $fixture.FullName 'tools'
    New-Item -ItemType Directory -Path $tools | Out-Null
    $wrapper = Join-Path $tools 'Test-RecordStoreRoundTrip.ps1'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Test-RecordStoreRoundTrip.ps1') -Destination $wrapper
    $project = Join-Path $fixture.FullName 'copy.fwdata'
    New-Item -ItemType File -Path $project | Out-Null
    @'
param([string]$Project, [string]$Configuration, [string]$Filter, [switch]$All, [string]$ResultsSuffix)

$ErrorActionPreference = 'Stop'
$workspace = $env:MOTIF_RECORD_STORE_CEBUANO_WORKSPACE
$stage = $env:MOTIF_RECORD_STORE_CEBUANO_STAGE
$mode = $env:MOTIF_TEST_ROUND_TRIP_OUTCOME
$resultsName = if ($ResultsSuffix) { "test-results-$ResultsSuffix" } else { 'test-results' }
$results = Join-Path $PSScriptRoot "bin/$Configuration/$resultsName/$Project"
New-Item -ItemType Directory -Force -Path $results | Out-Null
$outcome = if ($mode -eq 'skip') { 'NotExecuted' } else { 'Passed' }
$testName = 'SIL.Motif.Tests.RecordStore.RoundTripTests.CebuanoCopySurvivesStoreReopenAndLcmSerialization'
Set-Content -LiteralPath (Join-Path $results "$Project.trx") -Value @"
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results><UnitTestResult testName="$testName" outcome="$outcome" /></Results>
</TestRun>
"@
if ($mode -ne 'no-marker') {
    Set-Content -LiteralPath (Join-Path $workspace "$stage.complete") -Value $stage -NoNewline
}
if ($mode -eq 'pass') {
    if ($stage -eq 'import') {
        foreach ($directory in @('source', 'store', 'templates')) {
            New-Item -ItemType Directory -Path (Join-Path $workspace $directory) | Out-Null
            Set-Content -LiteralPath (Join-Path $workspace "$directory/probe") -Value $directory
        }
    } elseif (-not (Test-Path -LiteralPath (Join-Path $workspace 'source/probe')) -or
        -not (Test-Path -LiteralPath (Join-Path $workspace 'store/probe')) -or
        (Test-Path -LiteralPath (Join-Path $workspace 'templates/probe'))) {
        throw 'Verification needs the closed import artifacts and a private templates directory.'
    }
}
exit 0
'@ | Set-Content -LiteralPath (Join-Path $fixture.FullName 'test.ps1')

    foreach ($mode in @('skip', 'no-marker')) {
        $env:MOTIF_TEST_ROUND_TRIP_OUTCOME = $mode
        $output = & pwsh -NoProfile -File $wrapper -Configuration Release -CebuanoCopy $project 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0 -or $output -notmatch 'Cebuano import stage did not' -or $output -match 'Cebuano verify stage') {
            throw "The wrapper must reject the $mode import before verification.`n$output"
        }
    }
    Remove-Item -LiteralPath (Join-Path $fixture.FullName 'bin') -Recurse

    $env:MOTIF_TEST_ROUND_TRIP_OUTCOME = 'pass'
    $output = & pwsh -NoProfile -File $wrapper -Configuration Release -CebuanoCopy $project 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "The wrapper rejected two passing phases.`n$output" }
    $results = @(Get-ChildItem -LiteralPath (Join-Path $fixture.FullName 'bin/Release') -Filter '*.trx' -Recurse)
    foreach ($stage in @('import', 'verify')) {
        if (-not ($results.FullName -match "test-results-[^/\\]+-$stage[/\\]")) {
            throw "The wrapper did not retain the $stage TRX."
        }
    }
    Write-Host 'Round-trip wrapper rejected a skip-only result and a missing marker, then retained both passing phases.'
}
finally {
    $env:MOTIF_TEST_ROUND_TRIP_OUTCOME = $oldOutcome
    $fixture.Delete($true)
}
