# The scheduled slow run

`.github/workflows/slow.yml` runs every day at 04:47 UTC and on manual dispatch (Actions, "Slow scale run", Run
workflow). It sets `MOTIF_SLOW_SCALE=1` and runs the tests below on `ubuntu-22.04`. `./test.ps1 -All`, which is the
merge gate and what `ci.yml` runs, does not set that variable, so those tests report as skipped there. Only this
run proves them.

## What only the scheduled run proves

Ten tests in `SIL.Motif.Tests.App`, selected by the trait `Category=SlowScale` plus the one layout test named below.
Nine carry `[SlowScaleFact]`, which skips unless `MOTIF_SLOW_SCALE=1`, and the trait.

| Test | What it pins |
|---|---|
| `ProgressiveDisplayTests.ExpandedDerivationTreeNavigatesSixtyChildrenInEachDirection` | derivation tree realizes only nearby children over 60 steps |
| `ProgressiveDisplayTests.RecentProjectMenuReachesTheLastOfTwoThousandProjects` | the recent-project menu pages through 2,000 projects |
| `Walkthrough.BaselineTextWordsDeserializeScaleTests.TwentyOneThousandSixHundredFourRowSelectionReportsFullDeserializeCost` | deserialize cost of a 21,604-row selection |
| `Walkthrough.LargeProjectScaleTests.LargeProjectCompletesTheFakeParserWorkflowWithinTimeAndMemoryBudgets` | time and memory budgets at representative project size |
| `Walkthrough.SelectionViewportScaleTests.RepresentativeSequentialRangesKeepActualDisplayModelsAndReaderReadsBounded` | display models and reader reads stay bounded at 21,604 rows |
| `Walkthrough.UnparsedLargeProjectScaleTests.LargeSelectionOpensScrollsAndReopensWithinMemoryBudgetsWithoutParsing` | memory budgets for a large unparsed selection |
| `TextsProposalMemoryTests.AFlipOverASixHundredEightyEightThousandWordSelectionAlignsTheProposalOnceAndParsesNothing` | 688,000 words: a flip of the view aligns the Proposal once |
| `TextsProposalMovedChipMemoryTests.ASixHundredEightyEightThousandWordProposalCountsAndFiltersItsMovedWordsWithoutRowsAndLetsAllOfThemGo` | 688,000 words: the Moved chip makes no rows and keeps nothing |
| `TextsProposalChangesMemoryTests.ASixHundredEightyEightThousandWordProposalFindsItsMovesAsPositionsAndLetsAllOfThemGo` | 688,000 words: What Changed holds positions and counts and lets go |
| `LayoutInvariantSystemTests.EveryPageAndApplicableStateSatisfiesTheFullLightGrid` | the full layout grid |

The layout test also runs in the gate, but there it inspects only the tightest cell (640x600 at 150%). With
`MOTIF_SLOW_SCALE=1` it inspects all three cells (640x600 at 100%, 960x780 at 100%, 640x600 at 150%); the other two
run nowhere else. The three 688,000-word App tests are in the table because they were moved out of the gate;
landing no longer proves their memory limits, and a regression in them shows within a day instead of at landing.

`TextsProposalMemoryTests.ASixHundredEightyEightThousandWordTrialIsHeldAsValuesAWordWithoutAnyOfItsRows` also builds
688,000 words but takes about 5 seconds, so it stays in the gate.

## Runner memory

The three moved tests build their Trials as arrays of 688,000 `AssessmentWordResult` rows, which is most of what
they hold. Measured here (Debug build, one test host running all three, a loaded 20-processor machine): peak
resident set 1.2 GB. All ten tests together in one host peaked at 2.0 GB. The tests assert at most 40 MiB held above
the Trial and print far less (the `MEASURE` lines in the log). GitHub's published standard runners for a public
repository, `ubuntu-22.04` and `windows-latest`, have 4 vCPUs and 16 GB; for a private repository they have 2 vCPUs
and 8 GB. Either holds 2.0 GB with room to spare, so the three run on the hosted runner.

## Not in the run: the Commands Difference measurement

`ProposalDifferenceCommandsTests.ProducingADifferenceOverALargeWordListReportsMemoryAndRows` also skips without
`MOTIF_SLOW_SCALE=1` and builds a 688,000-word project store. It asserts only that producing the Difference
succeeds and prints its memory. It is hand-only: the workflow does not run it and nothing proves it.

The one local attempt did not finish. The test host went silent for the 10-minute hang timeout, on a machine at
load 90, and the largest process in that unfinished run reached 5.4 GB resident. That figure is not settled: it
comes from one run under heavy load that never completed, and the hang dump may have inflated it. It does show the
test could come close to the 8 GB of a private-repository runner, and its run time is unknown. Run it by hand with
the last command below. Add it to `slow.yml` and to `tools/Assert-SlowScaleRan.ps1` once a run on a quiet machine
finishes with a known peak.

## Running it on demand

```
$env:MOTIF_SLOW_SCALE = '1'
./test.ps1 -All -Project SIL.Motif.Tests.App -Filter 'Category=SlowScale|FullyQualifiedName~LayoutInvariantSystemTests.EveryPageAndApplicableStateSatisfiesTheFullLightGrid'
./tools/Assert-SlowScaleRan.ps1 -ResultsDirectory bin/Debug/test-results   # every test above passed
./test.ps1 -All -Project SIL.Motif.Tests.Commands -Filter 'Category=SlowScale'   # the Commands measurement, by hand only
```

`tools/Assert-SlowScaleRan.ps1` names the ten tests and fails unless each passed, because a skipped test reports
success. Add a new slow test to that list as well as marking it with `[SlowScaleFact]` and
`[Trait("Category", "SlowScale")]`.

## Corpus text authoring measurement

`AuthorTextScaleTests.DraftDryRunAndApplyATwoHundredFiftyTwoThousandWordText` is a separate, hand-only
measurement. It skips unless `MOTIF_TEXT_SCALE=1`; neither `MOTIF_SLOW_SCALE` nor the scheduled slow run enables it.
It generates 252,000 words in 36,000 sentences and 2,000 paragraphs at runtime, with Free Translation and Notes,
stages a real Draft, then runs Dry Run on a file copy and Apply. Its `TEXT_SCALE` output records stage times,
allocated bytes, retained managed heap, working set before and after, cumulative process peak working set,
and serialized input, Draft and Proposal sizes. It asserts counts and effects, not machine-dependent timing.

```powershell
$env:MOTIF_TEXT_SCALE = '1'
./test.ps1 -All -Project SIL.Motif.Tests.Commands -Filter 'FullyQualifiedName~AuthorTextScaleTests'
```

## Landing

The workflow has not yet run on GitHub: it was validated by parsing only, and the same filtered commands were run
locally. The first dispatch comes after this lands; check that run before relying on the schedule.

Landing runs whole projects, with no filter. The slow run is the one deliberate exception: the nine tests above that
skip without `MOTIF_SLOW_SCALE=1`, which include the three 688,000-word App tests, are not proven when a batch
lands, and neither are the two layout cells the gate leaves out. They are proven by the next scheduled run. Do not
run the slow set to land a batch, and do not treat its skips in `-All` as a gap to fill by hand. A slow-run failure
is a defect to file against the commit range since the last green run.
