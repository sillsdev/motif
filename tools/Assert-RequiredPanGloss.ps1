[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$criticalTests = @(
    'SIL.Motif.Tests.App.RealClient.SeededProjectRealTransferTests.AssessmentAndHandoffTransferTheRetainedResultWithoutChangingTheSavedProject',
    'SIL.Motif.Tests.Commands.AssessCommandTests.SupportedAssessmentRecordsRealTimingAndStatisticsWithOneInvocation',
    'SIL.Motif.Tests.PanGloss.PanGlossSurfaceTests.RealDescriptionContainsEveryTypedRequestAndFakeCommand',
    'SIL.Motif.Tests.Parser.RealParserTraceTests.ATracedWordReturnsTheVerbatimTreeAndAMatchingDerivedSummary',
    'SIL.Motif.Tests.Parser.ParserSeamIntegrationTests.EveryMorphologyIdentityNamesAnObjectInTheParsedProject'
)

if (-not (Test-Path -LiteralPath $ResultsDirectory -PathType Container)) {
    throw "Test result directory does not exist: $ResultsDirectory"
}
$trxFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -File -Recurse |
    Where-Object Extension -eq '.trx')
if ($trxFiles.Count -eq 0) {
    throw "No fresh TRX files were found under $ResultsDirectory."
}

$results = @(
    foreach ($file in $trxFiles) {
        [xml] $trx = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($result in $trx.SelectNodes("//*[local-name()='UnitTestResult']")) {
            [pscustomobject]@{
                Name = [string] $result.GetAttribute('testName')
                Outcome = [string] $result.GetAttribute('outcome')
                Detail = $result.OuterXml
                File = $file.FullName
            }
        }
    }
)

$missingParserSkips = @($results | Where-Object {
    $_.Outcome -eq 'NotExecuted' -and $_.Detail -match '(?i)pangloss not found'
})
if ($missingParserSkips.Count -gt 0) {
    $names = ($missingParserSkips | ForEach-Object Name | Sort-Object -Unique) -join ', '
    throw "Pinned PanGloss integration was skipped because the parser artifact was missing: $names"
}

$unverified = @(
    foreach ($testName in $criticalTests) {
        $matching = @($results | Where-Object {
            $_.Name -ceq $testName -or $_.Name.EndsWith('.' + $testName, [System.StringComparison]::Ordinal)
        })
        if (-not ($matching | Where-Object Outcome -eq 'Passed')) {
            $outcomes = if ($matching.Count -eq 0) { 'no result' } else {
                ($matching | ForEach-Object Outcome | Sort-Object -Unique) -join ', '
            }
            [pscustomobject]@{ Name = $testName; Outcome = $outcomes }
        }
    }
)
if ($unverified.Count -gt 0) {
    $details = ($unverified | ForEach-Object { "$($_.Name) ($($_.Outcome))" }) -join '; '
    throw "Critical pinned PanGloss integration tests did not pass: $details"
}

$skips = @($results | Where-Object Outcome -eq 'NotExecuted')
$skipCategories = [ordered]@{ capability = 0; platform = 0; artifact = 0; other = 0 }
foreach ($skip in $skips) {
    if ($skip.Detail -match '(?i)Tagalog.*(?:Natural.?Class|copy.pattern)|(?:Natural.?Class|copy.pattern).*Tagalog|unsupported.*copy.pattern') {
        $skipCategories.capability++
    }
    elseif ($skip.Detail -match '(?i)platform|Windows|Linux|macOS|link privilege|symbolic link') {
        $skipCategories.platform++
    }
    elseif ($skip.Detail -match '(?i)opt.in|artifact|not configured') {
        $skipCategories.artifact++
    }
    else {
        $skipCategories.other++
    }
}
Write-Output "Required PanGloss integration tests passed: $($criticalTests.Count)."
Write-Output "NotExecuted skip categories: capability=$($skipCategories.capability), platform=$($skipCategories.platform), artifact=$($skipCategories.artifact), other=$($skipCategories.other)."
