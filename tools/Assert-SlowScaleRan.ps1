[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory
)

# A skipped test reports success, so this names each test only the slow run proves and fails unless it passed.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$slowTests = @(
    'SIL.Motif.Tests.App.ProgressiveDisplayTests.ExpandedDerivationTreeNavigatesSixtyChildrenInEachDirection',
    'SIL.Motif.Tests.App.ProgressiveDisplayTests.RecentProjectMenuReachesTheLastOfTwoThousandProjects',
    'SIL.Motif.Tests.App.Walkthrough.BaselineTextWordsDeserializeScaleTests.TwentyOneThousandSixHundredFourRowSelectionReportsFullDeserializeCost',
    'SIL.Motif.Tests.App.Walkthrough.LargeProjectScaleTests.LargeProjectCompletesTheFakeParserWorkflowWithinTimeAndMemoryBudgets',
    'SIL.Motif.Tests.App.Walkthrough.SelectionViewportScaleTests.RepresentativeSequentialRangesKeepActualDisplayModelsAndReaderReadsBounded',
    'SIL.Motif.Tests.App.Walkthrough.UnparsedLargeProjectScaleTests.LargeSelectionOpensScrollsAndReopensWithinMemoryBudgetsWithoutParsing',
    'SIL.Motif.Tests.App.TextsProposalChangesMemoryTests.ASixHundredEightyEightThousandWordProposalFindsItsMovesAsPositionsAndLetsAllOfThemGo',
    'SIL.Motif.Tests.App.TextsProposalMemoryTests.AFlipOverASixHundredEightyEightThousandWordSelectionAlignsTheProposalOnceAndParsesNothing',
    'SIL.Motif.Tests.App.TextsProposalMovedChipMemoryTests.ASixHundredEightyEightThousandWordProposalCountsAndFiltersItsMovedWordsWithoutRowsAndLetsAllOfThemGo',
    'SIL.Motif.Tests.App.LayoutInvariantSystemTests.EveryPageAndApplicableStateSatisfiesTheFullLightGrid'
)

if (-not (Test-Path -LiteralPath $ResultsDirectory -PathType Container)) {
    throw "Test result directory does not exist: $ResultsDirectory"
}
$trxFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -File -Recurse | Where-Object Extension -eq '.trx')
if ($trxFiles.Count -eq 0) { throw "No TRX files were found under $ResultsDirectory." }

$results = @(
    foreach ($file in $trxFiles) {
        [xml] $trx = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($result in $trx.SelectNodes("//*[local-name()='UnitTestResult']")) {
            [pscustomobject]@{ Name = [string] $result.GetAttribute('testName'); Outcome = [string] $result.GetAttribute('outcome') }
        }
    }
)

$unverified = @(
    foreach ($testName in $slowTests) {
        $matching = @($results | Where-Object { $_.Name -ceq $testName })
        if (-not ($matching | Where-Object Outcome -eq 'Passed')) {
            $outcomes = if ($matching.Count -eq 0) { 'no result' } else { ($matching | ForEach-Object Outcome | Sort-Object -Unique) -join ', ' }
            "$testName ($outcomes)"
        }
    }
)
if ($unverified.Count -gt 0) { throw "Slow-scale tests that did not pass: $($unverified -join '; ')" }
Write-Output "Slow-scale tests passed: $($slowTests.Count)."
