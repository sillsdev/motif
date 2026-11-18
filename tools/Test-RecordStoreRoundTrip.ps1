<#
  .DESCRIPTION
  Cebuano requires this staged Release wrapper so each phase has its own test host and timeout.

  .EXAMPLE
  pwsh -NoProfile -File ./tools/Test-RecordStoreRoundTrip.ps1 -Configuration Release -CebuanoCopy '/private/copies/ceb/ceb.fwdata'
#>
[CmdletBinding()]
param(
    [string]$IndonesianCopy,
    [string]$CebuanoCopy,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($IndonesianCopy) -and [string]::IsNullOrWhiteSpace($CebuanoCopy)) {
    throw 'Specify -IndonesianCopy or -CebuanoCopy with an external .fwdata copy.'
}
if (-not [string]::IsNullOrWhiteSpace($CebuanoCopy)) {
    if (-not $PSBoundParameters.ContainsKey('Configuration')) { $Configuration = 'Release' }
    if ($Configuration -ne 'Release') { throw 'The staged Cebuano round trip requires -Configuration Release.' }
}
foreach ($inputPath in @($IndonesianCopy, $CebuanoCopy)) {
    if ([string]::IsNullOrWhiteSpace($inputPath)) { continue }
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf) -or
        [IO.Path]::GetExtension($inputPath) -ne '.fwdata') {
        throw "Round-trip gate requires an external .fwdata copy: $inputPath"
    }
}
$oldIndonesian = $env:MOTIF_RECORD_STORE_INDONESIAN
$oldCebuano = $env:MOTIF_RECORD_STORE_CEBUANO
$oldWorkspace = $env:MOTIF_RECORD_STORE_CEBUANO_WORKSPACE
$oldStage = $env:MOTIF_RECORD_STORE_CEBUANO_STAGE
$test = Join-Path $PSScriptRoot '../test.ps1'
$workspace = $null
try {
    if (-not [string]::IsNullOrWhiteSpace($IndonesianCopy)) {
        $env:MOTIF_RECORD_STORE_INDONESIAN = (Resolve-Path -LiteralPath $IndonesianCopy).Path
        $env:MOTIF_RECORD_STORE_CEBUANO = $null
        & $test -Project SIL.Motif.Tests.RecordStore -Configuration $Configuration
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    if (-not [string]::IsNullOrWhiteSpace($CebuanoCopy)) {
        $workspace = [IO.Directory]::CreateTempSubdirectory('motif-cebuano-roundtrip-')
        $importWorkspace = $workspace.CreateSubdirectory('import')
        $verifyWorkspace = $workspace.CreateSubdirectory('verify')
        $runId = [guid]::NewGuid().ToString('N')
        $project = 'SIL.Motif.Tests.RecordStore'
        $testName = "$project.RoundTripTests.CebuanoCopySurvivesStoreReopenAndLcmSerialization"
        $env:MOTIF_RECORD_STORE_CEBUANO = (Resolve-Path -LiteralPath $CebuanoCopy).Path
        foreach ($phase in @($importWorkspace, $verifyWorkspace)) {
            $stage = $phase.Name
            Write-Host "Cebuano $stage stage"
            $env:MOTIF_RECORD_STORE_CEBUANO_STAGE = $stage
            $env:MOTIF_RECORD_STORE_CEBUANO_WORKSPACE = $phase.FullName
            $suffix = "cebuano-$runId-$stage"
            & $test -Project $project -Configuration Release -All -Filter "FullyQualifiedName=$testName" -ResultsSuffix $suffix
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

            $resultPath = Join-Path $PSScriptRoot "../bin/Release/test-results-$suffix/$project/$project.trx"
            if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
                throw "Cebuano $stage stage did not write a fresh result at $resultPath."
            }
            [xml]$trx = Get-Content -LiteralPath $resultPath -Raw
            $results = $trx.SelectNodes("//*[local-name()='UnitTestResult' and @testName='$testName']")
            if ($results.Count -ne 1 -or $results[0].GetAttribute('outcome') -ne 'Passed') {
                throw "Cebuano $stage stage did not pass $testName. See $resultPath."
            }
            $marker = Join-Path $phase.FullName "$stage.complete"
            if (-not [IO.File]::Exists($marker) -or [IO.File]::ReadAllText($marker) -cne $stage) {
                throw "Cebuano $stage stage did not write its completion marker. See $resultPath."
            }
            Write-Host "Cebuano $stage passed. Result saved at $resultPath"
            if ($stage -eq 'import') {
                Move-Item -LiteralPath @((Join-Path $phase.FullName 'source'), (Join-Path $phase.FullName 'store')) `
                    -Destination $verifyWorkspace.FullName
            }
        }
    }
}
finally {
    $env:MOTIF_RECORD_STORE_INDONESIAN = $oldIndonesian
    $env:MOTIF_RECORD_STORE_CEBUANO = $oldCebuano
    $env:MOTIF_RECORD_STORE_CEBUANO_WORKSPACE = $oldWorkspace
    $env:MOTIF_RECORD_STORE_CEBUANO_STAGE = $oldStage
    if ($null -ne $workspace) { $workspace.Delete($true) }
}
