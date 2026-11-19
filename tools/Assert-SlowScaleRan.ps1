[CmdletBinding()]
param(
    [string] $ResultsDirectory,

    # Where the test sources live; the list below is checked against them before any result is read.
    [string] $TestsRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'tests'),

    # Check only that every listed test still exists and is slow-gated, without reading results.
    [switch] $ListOnly
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
    'SIL.Motif.Tests.App.TextsProposalMemoryTests.AStoredTrialIndexLooksUpASixHundredEightyEightThousandWordSelection',
    'SIL.Motif.Tests.App.LayoutInvariantSystemTests.EveryPageAndApplicableStateSatisfiesTheFullLightGrid',
    'SIL.Motif.Tests.Commands.SourcePassageQueryTests.CebSizedIndexReportsWarmPageAndPendingPublicationCosts',
    'SIL.Motif.Tests.Commands.SourceApprovalTests.TenThousandApprovalsInOneApplyTakeSeconds',
    'SIL.Motif.Tests.Worker.Worker.QuerySnapshotStoreTests.OpensReadsAndQueriesThirtyOneThousandFormsWithinOneSecond',
    'SIL.Motif.Tests.Contract.Parsimony.FormQueryTests.ThirtyOneThousandFormsGroupWithinOneSecond'
)

# The layout test runs in the gate; every other listed test must exist and carry the SlowScale trait.
$gateTests = @('SIL.Motif.Tests.App.LayoutInvariantSystemTests.EveryPageAndApplicableStateSatisfiesTheFullLightGrid')
if (-not (Test-Path -LiteralPath $TestsRoot -PathType Container)) { throw "Test source directory does not exist: $TestsRoot" }
$sources = @(Get-ChildItem -LiteralPath $TestsRoot -Filter '*.cs' -File -Recurse | ForEach-Object { [pscustomobject]@{ Lines = [IO.File]::ReadAllLines($_.FullName) } })
$stale = @(
    foreach ($testName in $slowTests) {
        $parts = $testName.Split('.')
        $method = $parts[-1]; $class = $parts[-2]
        $classPattern = "\b(class|record)\s+$([regex]::Escape($class))\b"
        $methodPattern = "\b$([regex]::Escape($method))\s*\("
        $found = $false; $slow = $false
        foreach ($source in $sources | Where-Object { $_.Lines -match $classPattern }) {
            for ($i = 0; $i -lt $source.Lines.Count; $i++) {
                if ($source.Lines[$i] -notmatch $methodPattern -or $source.Lines[$i] -notmatch '^\s*(public|internal)\b') { continue }
                $found = $true
                for ($j = $i - 1; $j -ge 0 -and $source.Lines[$j] -match '^\s*(\[|///)'; $j--) {
                    if ($source.Lines[$j] -match 'Category",\s*"SlowScale"') { $slow = $true }
                }
            }
        }
        if (-not $found) { "$testName (no such test)" }
        elseif (-not $slow -and $gateTests -notcontains $testName) { "$testName (not marked Category=SlowScale)" }
    }
)
if ($stale.Count -gt 0) { throw "The slow-scale list names tests that are gone or no longer slow: $($stale -join '; ')" }
if ($ListOnly) { Write-Output "Slow-scale list matches the tests: $($slowTests.Count)."; return }

if (-not $ResultsDirectory) { throw 'ResultsDirectory is required unless -ListOnly is given.' }
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
