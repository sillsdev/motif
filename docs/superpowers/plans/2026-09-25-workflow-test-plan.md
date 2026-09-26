# Workflow test plan: what a linguist does, and where each part is tested

**In plain terms:** Motif's tests are being rearranged around what a linguist actually does: open a
project, measure it, read the results, collect changes and apply them to FieldWorks. Each thing the linguist
relies on is checked as close as possible to the code that decides it, so that the tests keep passing when
the window is redesigned, and only five short tests drive the whole window to prove its parts are
connected. The plan also lists the buttons and commands these workflows need that Motif lacks, and the bugs
found while planning, so they can be fixed before the tests that depend on them are written.

**Status:** plan, 2026-09-25. It reconciles seven planning reports and the architecture review A2, whose
findings F01–F17 are being built now in eight batches. A test that needs a finding says so, by its number.
It follows ADR 0041 (the database is the only store), ADR 0043 (one command catalog, two front ends) and
ADR 0046 (pages, not stages). It supersedes the walkthrough plan of 2026-09-16
(`2026-09-16-workflow-walkthrough-tests.md`): that plan's walkthroughs become the smoke tests of §1.3 or move
below the window (§3.1), and its `CommandClient(managedRoot)` seam gives way to F08's composition root.

**How to read the ids.** Each workflow has an id made of a journey word and a number: `OPEN-`, `RUN-`,
`READ-`, `APPLY-`, `AGENT-`, `RES-`. They replace the seven reports' own numbering, which clashed with each
other and with the architecture findings; §2.7 maps every old id to its new one. **Never write a workflow
id, a finding id or a smoke-journey number in code, a test name, a comment or a string literal.** The
comment gate bans plan references and fails the build. Name tests for what they prove.

**Words.** The window and the CLI name things differently on purpose (ADR 0046 decision 3). The window says
"Review changes", "Apply to FieldWorks project", "N changes not applied yet", "no longer fits" and "AI
Handoff". The CLI says `preflight`, `apply`, a Draft Proposal and a drifted change. On-screen words proposed
here never include Proposal, Draft, Preflight or a `motif …` command line.

## 1. The three test levels, and how to choose one

Motif can be tested at three levels: the whole window, one real piece of Motif working against a real
project, or a single rule on its own. The more of Motif a test runs, the more it proves about how the parts
connect, and the more often it breaks when the window changes. So each check goes to the smallest level
that still proves what the linguist relies on.

### 1.1 The levels

| Level | What runs | What it may assert | What it must not assert |
|---|---|---|---|
| **S**: headless full app | The window built by `MotifAppComposition.Create` (F08): the real `MainWindow`, the real `CommandClient` and command catalog, a seeded project on disk with its Motif store, `InProcessRunnerLauncher` draining the real job handlers, `FakePanGloss`, scripted pickers and drag source, and a fixed `TimeProvider`. It is driven through accessible names by `WalkthroughWindow`. | That a person can find the next control, activate it, and reach the next usable state: launch, composition, navigation, one real round trip, "can proceed". | Numbers, sentences, parser output, persistence details, refusal variants. |
| **I**: one real seam, no full window | Exactly one of: **(a)** the CLI end to end: the built `motif.exe` as a child process, found through `BuildOutput`, read by exit code and `--json`; **(b)** a page model or view model over the real `CommandClient` and a seeded project; **(c)** a command over a real seeded `LcmCache` and Motif store, including the command-level tests that live in `Tests.Cli`; **(d)** the worker end to end: a real runner process against a seeded store. | Data read from or written to a project or store, process and cancellation behaviour, typed refusals, concurrency, parser-boundary behaviour. | Layout, styling, wording beyond a refusal code. |
| **U**: unit | A rule, projection or state machine; a view model, page model or window over `FakeCommandClient`; a focused Avalonia component; a repository, or a query that opens no `LcmCache`, over a temporary SQLite file; the parser boundary against the `FakePanGloss` process. | Enabled states, wording, ranking, classification, transitions, mapping a supplied result to what is shown. | Anything a fake response fabricates, such as "the project drifted" or "the Trial measured every changed word". |

Three consequences follow, and they settle disagreements between the reports:

- **A window over `FakeCommandClient` is U.** It proves binding and nothing about data. Most of
  `MainWindowSmokeTests` is this, whatever its name says; one report called it I.
- **S never crosses the real worker process.** It drains jobs in process through `InProcessRunnerLauncher`.
  The process path is owned at I by `RunnerSpineTests` and by the CLI tests in `AGENT-03` and `RES-02`.
- **S never uses the real parser.** `RealParserFactAttribute` skips when PanGloss is not found, which it
  silently is in every `motif.worktrees\*` checkout (deferred questions, "Parser discovery"). A smoke test
  that skips is no smoke test. Real-parser checks form one I lane of their own (`RUN-11`), run as a separate
  CI step whose skip count is printed.

A fourth lane, native Windows UI Automation for screen-reader names, focus and OS dialogs, is not counted
as a level here; §8 question 1 asks whether to build it.

### 1.2 The rule for choosing a level

1. Write the invariant in the linguist's or the agent's words: "after I cancel, nothing half-done is kept".
2. If it is a rule over typed values (enabled or not, wording, ranking, classification, a state
   transition), test it at **U**.
3. If it needs a real `LcmCache` and Motif store, a Motif child process (the CLI or the runner), cancellation
   cleanup across the real client, or two writers at once, test it at **I**, at the lowest seam that has
   the real thing. The parser boundary against the `FakePanGloss` process, and a repository over a
   temporary SQLite file, stay **U**.
4. Use **S** only when the failure could come from composition, navigation, a binding to the real client,
   or the return path from a real command. Add it as a step in an existing S journey, not as a new test.
5. **A fake response never proves a data claim.** Every U page-model rule that rests on command data needs
   an I counterpart over the real client. The owner recorded why: fake responses hid "Fix these first" being
   always empty (deferred questions).
6. Use the real parser only when the claim is about PanGloss's own output or compatibility, and then at I
   in the real-parser lane.
7. A claim about a platform (screen-reader tree, keyboard focus order, an OS dialog, pixels) belongs to the
   native lane or a person's check. A headless result never counts as proof of it.
8. Assert a detail at one level only. A higher level asserts "reached" or "shown", never "equals 42".

### 1.3 The smoke budget

**Five S tests. The whole S suite finishes within 3 minutes on the CI runner in Release; each test within
60 seconds; each step within a named 30-second limit.** No retries count as a pass. A sixth S test replaces
one of the five, or needs the owner's agreement.

The reports proposed three (R1), four (R3) and three (Area B). Five is those, minus R3's "agent beside the
window", plus the FieldWorks journey F08 already specifies:

- **Dropped:** the agent-beside-the-window journey. Its failure is the window not reloading what the CLI
  wrote. That has an I owner, `PendingChangesCliAppTests`, plus the activation test in `AGENT-03`, and a U
  binding test for `Activated`. Nothing about it needs the whole window.
- **Kept:** the FieldWorks journey. It is the one place where a real file state (a held lock, a later save)
  must reach a disabled button and a freshness line through the real composition. F08 names its two halves.

| Smoke test (new file under `tests/SIL.Motif.Tests.App/App/Smoke/`) | The person's route | Workflows | Needs |
|---|---|---|---|
| `FirstProjectSmokeTests.AFirstProjectOpensCapturesSetsUpAndShowsItsFirstRun` | Launch with an empty Motif root → Select a new project (scripted picker) → Refresh → setup → Finish → the run completes (fake parser) → Overview shows a result → Texts opens | `OPEN-01`, `OPEN-02`, `RUN-01`, `READ-01` | F08 |
| `CancelRunSmokeTests.CancellingARunLeavesTheWindowReadyToRunAgain` | Open a seeded project → Run with a held fake parser → progress shows → Cancel → cancelled state → Run again → completes | `RUN-02` | F08 |
| `ChangeToReceiptSmokeTests.ACollectedChangeIsCheckedAppliedAndShowsItsReceipt` | Seeded project with stored evidence → Texts → collect one change → Review changes → Check these changes → Apply to FieldWorks project → the Receipt shows and the badge clears | `APPLY-01`, `APPLY-04`, `APPLY-05` | F02, F08 |
| `ReturnToProjectSmokeTests.ARelaunchedWindowReopensAKnownProjectAndSwitchesToAnother` | Compose, open project A, collect a change, dispose; compose again over the same root → Open recent → A's stored evidence and pending count show → switch to B → B's state shows | `OPEN-05`, `OPEN-06` | F04, F05, F08 |
| `FieldWorksBesideMotifSmokeTests.AHeldProjectBlocksApplyAndALaterSaveAsksForRefresh` | Pending change → FieldWorks holds the project → Review changes shows why Apply is blocked → release → FieldWorks saves → window activation → "FieldWorks saved since" → Refresh | `APPLY-07`, `RUN-03` | F01, F08 (it merges F08's `FieldWorksHoldingTheProjectShowsHeldAndBlocksApply` and `AFieldWorksSaveShowsSavedSince` into one journey) |

The first three are the **minimal P0 smoke suite** and are built first (§7, phase 1).

## 2. Workflows, by what the person is doing

This is the list of things a linguist, or an AI agent using the CLI, needs to do, grouped the way their
day goes. For each one it says which part is checked at which level, which tests already do it, what is
missing, and what the tests need from the product or from the architecture work.

**How each workflow reads.** One workflow is one person's goal. A refusal or error variant is a row in its
table, not another workflow. Each row gives:

- **Level**: exactly `S`, `I` or `U` (§1.1).
- **Test**: `FileName.MethodName`. A new file is named for its subject, never for a workflow id.
- **Parser**: `Yes` only for the real PanGloss. `No` includes the fake, and the fixture says which fake.
- **Fixture**: a short name from §4.6.
- **State**: `new`, `keep`, `moves` (from an existing test, named in §3), or `F0n` (the test arrives with
  that architecture finding, which names it; do not write it twice).

A row whose test is `(smoke)` is one step inside a §1.3 smoke test, not a test of its own.

**In numbers:** 57 workflows (20 P0, 28 P1, 9 P2) and 249 assertion rows: 13 S (steps inside the five
smoke tests), 125 I and 111 U. Of those rows, 92 are new tests, 110 keep an existing test, 17 move one, and
30 arrive with an architecture finding.

### 2.1 Open and set up a project, and find your way around

#### OPEN-01 · P0 · Open a project for the first time

*As a linguist, I choose my project, Motif captures its Baseline from FieldWorks' last save, and I can go on
to set it up.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| The window starts with an empty Motif root, the project menu works, and no numbers are invented | S | (smoke) `FirstProjectSmokeTests` | No | empty root, `SeededProject` | new |
| Browse emits the chosen path; cancelling the picker leaves the open project alone | U | `ProjectViewModelTests` (browse and cancel cases) | No | scripted picker | keep |
| A refused Known-project list at startup still leaves Browse working | U | `KnownProjectsRefreshTests.ARefusedKnownProjectListLeavesBrowseAvailable` | No | fake | new |
| Capture reads a saved copy, never writes the `.fwdata`, records the Known project, and the Text inventory matches the Baseline | I | `FirstOpenRealClientTests.CapturingAFirstBaselineRecordsTheProjectAndLeavesItsFileUntouched` | No | `SeededProject` | moves |
| A missing `.fwdata` is a typed refusal on the freshness line, and Browse still works | I | `FirstOpenRealClientTests.AMissingProjectFileIsRefusedOnTheFreshnessLine` | No | `SeededProject`, file deleted | new |
| Capture identity, freshness and lock reporting | I | `BaselineCaptureCommandTests` | No | `SeededProject` | keep |
| Setup opens after the first Baseline when nothing is configured | U | `HandoffWorkspaceViewModelTests.AProjectWithoutADefaultSelectionOpensSetupOnItsFirstStep` | No | fake | keep |
| A project opened this session is under Open recent at once, without a restart | U | `KnownProjectsRefreshTests.AProjectOpenedThisSessionJoinsOpenRecentAtOnce` | No | fake | new |

- **Existing tests:** `W1ChooseProjectAndCaptureBaselineTests.ChoosingProjectCapturingBaselineMakesAssessmentRunnable`
  (a hand-built window, which becomes the smoke test); `AppSmokeTests.MainWindowCanBeConstructedHeadlessly`;
  `KnownProjectsQueryTests`; `CurrentBaselineQueryTests`; `BaselineCaptureArgvTests`.
- **Gap:** no test runs the App's own composition (`src/SIL.Motif.App/App.axaml.cs:14-44`). The walkthrough
  copies it by hand (`tests/SIL.Motif.Tests.App/App/Walkthrough/WalkthroughWindow.cs:21-39`).
- **Missing capability:** none. The stale Open recent list is bug 2 (§6).
- **Depends on:** F08 for the S row. The Open recent refresh touches the shell, so it waits for F05.

#### OPEN-02 · P0 · Choose what to measure, and run for the first time

*As a linguist, I pick the Texts and words to measure every time, set the per-word time and step limits, and
Finish starts the first run.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Finish starts a run that reaches a result | S | (smoke) `FirstProjectSmokeTests` | No | `FakePanGloss` | new |
| Finish is unavailable until the last step; a first run can have no step limit | U | `HandoffWorkspaceViewModelTests.FirstRunCannotStartBeforeTheLastSetupStep`, `.FirstRunCanUseNoStepLimit` | No | fake | keep |
| Finishing saves the exact Text ids, added words and both limits, and a new window over the same store reads them back | I | `SetupRealClientTests.FinishingSetupSavesTheSelectionAndLimitsAndANewWindowReadsThemBack` | No | `SeededProject` | new |
| The saved Default Selection and limits reach the run; request limits win over configured ones | I | `AssessCommandTests.DefaultSelectionFeedsAssessOverviewAndStoredTiming`, `.TheRunUsesRequestLimitsBeforeProjectConfiguredLimits` | No | `SeededProject`, fake assessor | keep |
| Texts, typed words and All wordforms combine without measuring a word twice; typed words are NFD; an unknown Text is refused | I | `SelectionComposerTests` | No | `SeededProject` | keep |
| Pasted words are trimmed and blank lines dropped before the request is built | U | `SelectionViewModelTests` | No | fake | keep |
| `selection set-default` stores the limits | I | `SelectionSetupArgvTests.SetDefaultPersistsTheChosenTimeAndStepLimits` | No | `SeededProject` | keep |

- **Existing tests:** `HandoffWorkspaceViewModelTests.FirstRunSavesTheSelectionAndUsesItWithTheChosenStepLimit`
  (fake client), `SelectionComposerTests`, `SelectionSetupArgvTests`.
- **Gap:** nothing proves through the real client that setup's choices survive into a new window.
- **Missing capability:** none.
- **Depends on:** F11 reshapes Setup into `SetupInput` and `SetupResult`. Write `SetupRealClientTests` after
  batch 5, against the result the shell saves, so F11 does not break it.

#### OPEN-03 · P0 · Skip setup, and not be asked again

*As a linguist, I can skip setup now; Motif does not ask again for this project, and Configure still opens
it.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Skip leaves a usable Overview, starts no run, and saves no Default Selection | U | `HandoffWorkspaceViewModelTests.SkippingFirstSetupDoesNotSaveADefaultSelection` | No | fake | keep |
| A stored Skip keeps setup closed on the next open, and Configure reopens it | U | `HandoffWorkspaceViewModelTests.StoredSkipSuppressesFirstOpenButConfigureCanReopenSetup` | No | fake | keep |
| Skip is stored per project: a new window over the same store does not open setup, and a second project still does | I | `SetupRealClientTests.ASkipIsKeptForThatProjectOnly` | No | `TwoProjects` | new |
| `setup skip` stores Skip without a Default Selection | I | `SelectionSetupArgvTests.SkippingSetupPersistsWithoutCreatingADefaultSelection` | No | `SeededProject` | keep |

- **Existing tests:** as in the table.
- **Gap:** the per-project Skip is proven only through a fake client.
- **Missing capability:** none. **Depends on:** F11, as `OPEN-02`.

#### OPEN-04 · P1 · Change what is measured later

*As a linguist, I open Configure…, see what I chose before, change it, and the next run uses the change.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Configure opens without leaving the current page | U | `HandoffWorkspaceViewModelTests.ConfigureOpensSetupWithoutNavigatingAwayFromTheCurrentPage` | No | fake | keep |
| A limit that is not a positive number, or a step limit that is not a whole number, cannot be saved | U | `SetupLimitsTests.ALimitThatIsNotAPositiveWholeNumberCannotBeSaved` | No | fake | new |
| Configure shows the stored Texts and limits; changing them makes the next run use the new ones | I | `SetupRealClientTests.ConfigureShowsTheStoredChoicesAndTheNextRunUsesTheNewOnes` | No | `SeededProject`, `FakePanGloss` | new |
| A Refresh keeps a checked Text the new Baseline still holds | U | `HandoffWorkspaceViewModelTests.RefreshingKeepsACheckedTextTheNewBaselineStillHoldsAndTheOtherSources` | No | fake | keep |
| Clicking Configure… in the project menu reopens setup after Skip and after a Refresh | S | `ConfigureWalkthroughTests.ConfigureReopensSetupAfterItWasSkippedAndAfterARefresh` | No | `SeededProject`, `FakePanGloss` | new |
| Clicking Configure… shows the saved Selection after Finish, a Refresh, and reopening from Open recent | S | `ConfigureWalkthroughTests.ConfigureShowsTheSavedSelectionAfterFinishARefreshAndARestart` | No | `SeededProject`, `FakePanGloss` | new |
| Before the first Refresh, Configure… is unavailable and says to refresh first | S | `ConfigureWalkthroughTests.BeforeTheFirstRefreshConfigureIsUnavailableAndSaysToRefreshFirst` | No | `SeededProject`, `FakePanGloss` | new |
| Configure opens with the saved values after Finish, Skip, a Refresh and a reopen, and not before a Baseline | U | `WorkspaceShellViewModelTests.ConfigureAfter*`, `WithoutABaselineConfigureIsUnavailableAndSaysToRefreshFirst` | No | fake | new |

- **Existing tests:** `HandoffWorkspaceViewModelTests.LaterRunsUseLimitsSavedWithTheDefaultSelection`.
- **Gap:** no limit validation test; no real-client round trip. **Missing capability:** none.
- **Depends on:** F11.

#### OPEN-05 · P0 · Come back to a project after closing Motif

*As a linguist, I close Motif, open it tomorrow, pick my project from Open recent, and find its numbers and
my changes not applied yet, exactly as I left them.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| A window composed again over the same root lists the project under Open recent, reopens it, and shows its stored numbers and pending count | S | (smoke) `ReturnToProjectSmokeTests` | No | `SeededEvidence`, `PendingChange` | new |
| Stored evidence reaches Texts, Timing and Try a Word even with no Overview page registered | I | `StoredEvidenceReachesEveryPageWithoutTheOverviewPage` | No | `SeededEvidence` | F04 |
| A new window over the same store shows the same changes, Default Selection, Skip and Baseline, and creates no second Baseline or change | I | `ReopenRealClientTests.ANewWindowOverTheSameStoreShowsWhatTheLastOneLeft` | No | `SeededEvidence`, `PendingChange` | moves |
| A change collected through the CLI appears on opening and after a restart | I | `PendingChangesCliAppTests.CliDraftAppearsInAppAfterOpeningAndAfterRestart` | No | `SeededProject` | keep |
| Known projects are listed most recent first; one whose file has gone is forgotten | U | `KnownProjectsQueryTests.ListsKnownProjectsMostRecentlySeenFirst`, `.OmitsAndForgetsAProjectWhoseFileHasGoneMissing` | No | temp machine store | keep |
| A project whose file went missing leaves Open recent when the list reloads, without a restart | U | `KnownProjectsRefreshTests.AProjectWhoseFileWentMissingLeavesOpenRecentOnReload` | No | fake | new |
| Opening restores the stored Matrix, Timing and AI Handoff scope | U | `WorkspaceContextTests.OpeningAProjectRestoresTheStoredMatrixAndHandoff`, `.OpeningAProjectReadsItsStoredTimingThroughThePageContext` | No | fake | keep |

- **Existing tests:** `RestartAndSwitchWalkthroughTests.RestartingAndSwitchingProjectsKeepsOnlyTheSelectedProjectState`
  "restarts" by building a second window in the same test process from a hand-copied composition; it is
  not a process restart and does not use the App's composition.
- **Gap:** stored evidence reaches the other pages only because Overview publishes it
  (`src/SIL.Motif.App/ViewModels/OverviewPageModel.cs`, F04). The Known-project list loads once, at
  startup (`src/SIL.Motif.App/App.axaml.cs:24`).
- **Missing capability:** reload of the Known-project list (bug 2). Whether to reopen on the last page is
  §8 question 5.
- **Depends on:** F04, F05, F08.

#### OPEN-06 · P0 · Switch between projects without mixing them up

*As a linguist with two projects, I switch from one to the other and back, and each shows only its own
numbers, Texts and changes.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| One switch shows the second project's name, Baseline and pending count | S | (smoke) `ReturnToProjectSmokeTests` | No | `TwoProjects` | new |
| Applying in one project does not mark the next one stale; a Review error does not follow; old changes are neither visible nor writable during the switch; every participant is cleared before any loads | U | `ProjectSwitchTests.ApplyingInOneProjectDoesNotMarkTheNextOneStale`, `.AReviewErrorDoesNotFollowTheNextProject`, `.NoPendingChangeOfTheOldProjectIsVisibleOrWritableDuringTheSwitch`, `.EveryParticipantIsClearedBeforeAnyLoads` | No | fake | F05 |
| Switching away and back keeps the first project's stored changes; a failed or cancelled switch deletes nothing | I | `ProjectSwitchTests.SwitchingAwayAndBackKeepsTheStoredDraft` | No | `TwoProjects` | F05 |
| After A → B → A, each shows only its own evidence, Text choices and changes, and neither `.fwdata` changed | I | `ProjectIsolationRealClientTests.EachProjectShowsOnlyItsOwnEvidenceTextsAndChanges` | No | `TwoProjects`, `SeededEvidence` | moves |
| Choosing a project from another page names it and returns to Overview | U | `WorkspacePageTests.ChoosingAProjectFromAnotherPageNamesItAndReturnsToOverview` | No | fake | keep |
| Switching clears the previous project's run and statistics state | U | `HandoffWorkspaceViewModelTests.ChoosingAnotherProjectClearsThePreviousProjectsAssessmentAndStatisticsState` | No | fake | keep |

- **Existing tests:** `WorkspaceContextTests.PendingChangesFollowAProjectSwitchOnlyThroughTheirSlotAndTheReviewBadgeCountsThem`
  (F05 rewrites it for the new order), `RestartAndSwitchWalkthroughTests`.
- **Gap:** state leaks between projects today (bug 3).
- **Missing capability:** none. **Depends on:** F05.

#### OPEN-07 · P1 · Try to switch while a run is going

*As a linguist, I cannot move to another project in the middle of a run; after I cancel, the new project
never shows the old run's answer.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| The project menu and Selection controls are disabled while a run is active and return when it ends | U | `WorkflowShellTests.ProjectMenuIsDisabledWhileAnAssessmentRunsAndReturnsWhenItEnds` | No | fake | keep |
| A run cancelled before a switch leaves nothing in the next project, and its parser process has exited | I | `ProjectIsolationRealClientTests.ARunCancelledBeforeASwitchLeavesNothingInTheNextProject` | No | `TwoProjects`, `FakePanGloss` hold | moves |

- **Existing tests:** `SwitchProjectWalkthroughTests.ProjectMenuIsDisabledDuringARunAndSwitchingAfterCancelClearsTheFirstProject`
  (real parser, whole window).
- **Gap:** the only end-to-end check needs the real parser, so it skips in most checkouts.
- **Missing capability:** none. Cancelling before a switch is the design
  (`src/SIL.Motif.App/ViewModels/HandoffWorkspaceViewModel.cs:308-325`).

#### OPEN-08 · P1 · A project with no Texts, or nothing measured yet

*As a linguist whose project has no Texts, or who has not run anything yet, I am told why a page is empty
and what I can do next.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| A project with no Texts can still choose typed words or All wordforms and Finish | I | `SetupRealClientTests.AProjectWithNoTextsCanStillChooseWordsAndFinish` | No | `NoTextsProject` | new |
| A captured project with no Texts reports an empty inventory, not "no Baseline" | I | `TextInventoryEmptyTests.ACapturedProjectWithNoTextsReportsAnEmptyInventory` | No | `NoTextsProject` | new |
| The reader says which case it is: no run yet, no Texts in the project, or Texts but none chosen | U | `EmptyStateTests.TheReaderSaysWhetherThereIsNoRunNoTextOrNoneChosen` | No | fake | new |
| Overview before any run shows no zeroes as results | U | `OverviewPageModelTests.WithoutAStoredRunNoResultIsShownAsZero` | No | fake | moves |
| Before any capture the inventory is empty | I | `TextInventoryQueryTests.BeforeAnyCaptureTheInventoryIsEmpty` | No | `SeededProject` | keep |

- **Existing tests:** `ResultsInTextViewModelTests.BeforeAnyAssessmentItSaysSoInsteadOfShowingNothing`,
  `MainWindowSmokeTests.OverviewWithoutStoredAssessmentDoesNotShowZeroesAsResults`.
- **Gap:** no fixture has a captured Baseline with zero Texts, so whether the reader confuses "no Texts" with
  "none chosen" is unconfirmed (§6).
- **Missing capability:** none known.

#### OPEN-09 · P1 · Move between pages without losing my place

*As a linguist, I go from Texts to Try a Word to Timing and back, and the tab, the chosen cell and the
checked words are as I left them; a word I chose stays the same word on every page.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| The sidebar opens pages built from the real registry | S | (smoke) `FirstProjectSmokeTests` | No | `SeededProject` | new |
| The Texts tab, chosen cell and checked words survive a visit to another page, until the evidence or the project changes | U | `PageStateRetentionTests.TextsChoicesSurviveAVisitToAnotherPage` | No | fake | new |
| A word from the real word list reaches Try a Word, Analyze texts and Timing as the same exact form, including one absent from the chosen Texts | I | `TextsRealClientTests.AWordFromTheRealWordListReachesEveryPageUnchanged` | No | `SeededEvidence` | new |
| Seven pages in registry order, opening on Overview | U | `WorkspacePageTests.TheSidebarListsTheSevenPagesInOrderAndOpensOnOverview`, `WorkflowShellTests.EveryPageHasExactlyOneRegistryEntryAndTheSidebarFollowsItsOrder` | No | fake | keep |
| A link on one page opens another through the page context alone | U | `WorkspaceContextTests.ALinkOnOnePageOpensAnotherThroughTheContextWithoutTheWorkspace` | No | fake | keep |

- **Existing tests:** `WorkspaceContextTests.TryingAWordThroughTheContextOpensTryAWordOnThatWord`,
  `ResultsInTextViewModelTests.AClickedWordOpensInTheWordsView_OnlyWhenAsked`.
- **Gap:** nothing states the retention rule; no real-client route test.
- **Missing capability:** none. **Depends on:** F11 moves page ownership; write the U test after batch 5.

#### OPEN-10 · P2 · Go Back and Forward between pages

*As a linguist, I press Back to return to the page I was just on, with its context, and Forward to go
again.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Back and Forward revisit pages with their focused word; a page the App opens by itself after a run is not a separate visit | U | `NavigationHistoryTests.BackAndForwardRevisitPagesWithTheirContext` | No | fake | new |
| Back and Forward are named, enabled only when there is somewhere to go, and work from the keyboard | U | `NavigationHistoryViewTests.BackAndForwardAreNamedEnabledAndKeyboardReachable` | No | fake | new |

- **Existing tests:** none. **Gap:** everything.
- **Missing capability:** Back and Forward (§5). Setup's own Back button is not page history.

#### OPEN-11 · P2 · Work in a narrow window

*As a linguist on a small screen, the sidebar folds to icons and every page is still one click away, with its
badge.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Below the threshold the sidebar shows icons with tooltips and badges, and every page opens | U | `WorkflowShellTests.ANarrowWindowCollapsesTheSidebarToIconsWithTooltipsAndKeepsTheBadges`, `MainWindowSmokeTests.AtTheNarrowestWindowEveryPageOpensOnAClickInTheCollapsedSidebar`, `WorkspacePageTests.TheSidebarCollapsesToIconsBelowItsWidthThreshold` | No | fake | keep |

- **Existing tests:** the three in the table overlap, and one pins pixel widths (§3).
- **Gap:** none beyond consolidation. **Missing capability:** none.

#### OPEN-12 · P1 · Reach everything by keyboard and screen reader

*As a linguist who uses the keyboard or a screen reader, I can open a project, move between pages, review my
changes and apply them without a mouse, and every control says what it is.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Every input and button declares an accessible name | U | `MainWindowSmokeTests.EveryInputAndButtonHasAnAccessibleName` | No | fake | keep |
| The project menu, sidebar, Review changes and Apply are reachable with Tab and Enter alone, with focus visible | U | `KeyboardRouteTests.TheCorePathCanBeWalkedWithTheKeyboardAlone` | No | fake | new |

- **Existing tests:** `WorkflowShellTests.PressingTheAllFilesButtonStartsADragOfEveryFileAndActivatingItFromTheKeyboardCopiesTheFolder`
  (one keyboard action).
- **Gap:** no keyboard route; the name check reads Avalonia metadata, not what Windows exposes to a screen
  reader. That needs the native lane (§8 question 1).
- **Missing capability:** a proven focus route through Review changes and Apply.

#### OPEN-13 · P2 · Light and dark

*As a linguist, when my system switches between light and dark, text, badges and my selection stay readable
and in place.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| A theme change updates representative Intent colours and keeps the selection and any refusal or progress shown | U | `ThemeSwitchTests.ChangingTheThemeUpdatesIntentColoursAndKeepsTheSelection` | No | fake | moves |
| Every Intent alias resolves in both themes | U | `DesignTokenTests` | No | none | keep |

- **Existing tests:** `MainWindowSmokeTests.SwitchingTheThemeVariantWithARefusalAndAnInProgressStateBoundRaisesNoException`
  asserts only that nothing throws.
- **Missing capability:** none. Motif follows the system theme (deferred questions, "Dark theme"); an
  in-window switch is not needed.

#### OPEN-14 · P2 · A large project and a long Text

*As a linguist with a big lexicon and long Texts, lists load, I can find my Texts, and a long Text scrolls
to its end without losing my place.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Text list, word list and Overview queries over a generated large project return every word; their times are recorded, not asserted | I | `LargeProjectTests.QueriesOverAGeneratedLargeProjectReturnEveryWord` | No | `LargeProject` | new |
| A run over a large Selection stores every word | I | `LargeProjectTests.ARunOverALargeSelectionStoresEveryWord` | No | `LargeProject`, `FakePanGloss` | new |
| In a long Text the last line is reachable, and filtering then clearing keeps the chosen word | U | `LongTextReaderTests.TheLastLineStaysReachableAndFilteringKeepsTheChosenWord` | No | fake | new |

- **Existing tests:** none at scale; `SeededProject` is deliberately tiny
  (`tests/SIL.Motif.Tests.Support/TestFixtures/SeededProject.cs:78`).
- **Gap:** no generated large fixture. Whether the reader needs virtualisation is unconfirmed (§6).
- **Missing capability:** the fixture (§4.6).

### 2.2 Measure the project

#### RUN-01 · P0 · Run an Assessment and see its result

*As a linguist, I press Run, watch it progress, and see the result on Overview and Texts; my FieldWorks
project is never touched.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Run reaches a completed result that Overview and Texts show | S | (smoke) `FirstProjectSmokeTests` | No | `FakePanGloss` | new |
| The run stores its Assessments, the resolved word list and timing; the source file is never written | I | `AssessCommandTests.DefaultSelectionFeedsAssessOverviewAndStoredTiming`, `SourceProjectIsNeverWrittenTests` | No | `SeededProject`, fake assessor | keep |
| Through the real client, a completed run is what Overview, Texts and Timing then read back | I | `RunRealClientTests.ACompletedRunIsWhatOverviewTextsAndTimingShow` | No | `SeededProject`, `FakePanGloss` | moves |
| The stored rows equal the rows the run returned: outcome, completion text, Fix these first, grades | I | `StoredRowsEqualTheRunRows` | No | `SeededProject` | F04 |
| Completed, refused and cancelled states map to what the run area shows | U | `AssessViewModelTests`, `CommandRunViewModelTests` | No | fake | keep |
| A run opens the Matrix on Texts | U | `WorkspacePageTests.ARunStartingOpensTheMatrixOnTheTextsPage` | No | fake | keep |

- **Existing tests:** `AssessmentWalkthroughTests.RunningAssessmentRendersReadingsAndPublishesStatistics`
  (real parser, whole window, exact readings and statistics).
- **Gap:** the only window-to-result check needs the real parser; its detail belongs lower (§3).
- **Missing capability:** none. **Depends on:** F04, F08.

#### RUN-02 · P0 · Stop a run, and start again

*As a linguist, I cancel a run that is taking too long; nothing half-done is kept, and the next run works.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Cancel reaches the running command, the window shows it stopped, and Run works again | S | (smoke) `CancelRunSmokeTests` | No | `FakePanGloss` hold | new |
| Cancelling while the assessor runs records no Assessment | I | `AssessCommandTests.CancellationWhileTheAssessorIsRunningRecordsNoAssessments` | No | `SeededProject`, fake assessor | keep |
| Through the real client, Cancel kills the parser process tree, keeps no retained invocation, and a rerun completes | I | `RunRealClientTests.CancellingKillsTheParserKeepsNothingAndARerunCompletes` | No | `FakePanGloss` hold | moves |
| The parser boundary stops the process and reports cancelled | U | `PanGlossInvokerTests.CancellationStopsTheParser_AndReportsCancelled` | No | `FakePanGloss` | keep |
| Cancelling a batch of Timing reruns stops the current word and starts no more | U | `WorkspaceContextTests.TimingCancelStopsTheCurrentWordAndDoesNotStartTheNextOne` | No | fake | keep |
| The same, through the real client: the parser stops and the next word never starts | I | `TimingRealClientTests.CancellingABatchOfRerunsStopsTheParserAndStartsNoMoreWords` | No | `FakePanGloss` hold | new |
| Cancelling a Refresh keeps the new Baseline, starts no run, and says the numbers are older | U | `WorkspacePageTests.WhileRefreshingTheLineSaysSoAndCancelStopsTheRun`, `.ARefreshWhoseAssessmentIsCancelledStillSaysTheNumbersAreOlder` | No | fake | keep |
| A cancellation before the command starts is still a typed refusal | U | `CommandClientCancellationTests.ACancellationBeforeTheCommandStartsIsStillATypedRefusal` | No | held client | keep |

- **Existing tests:** `CancelAssessmentWalkthroughTests.CancellingAssessmentLeavesNoInvocationAndAllowsARerun`
  (real parser, whole window).
- **Gap:** a fast, fake-parser cancellation through the real client does not exist.
- **Missing capability:** none. **Depends on:** F08.

#### RUN-03 · P0 · Notice that FieldWorks saved, and Refresh

*As a linguist, I edit in FieldWorks and come back; Motif says the numbers describe an older save, runs
nothing by itself, and Refresh brings them up to date.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Activating the window after a save shows "FieldWorks saved since", and Refresh captures and runs | S | (smoke) `FieldWorksBesideMotifSmokeTests` | No | `FieldWorksSimulator`, fixed clock | new |
| The project file's write time matches the Baseline until FieldWorks saves again | I | `CurrentBaselineQueryTests.TheProjectFilesWriteTimeMatchesTheBaselineUntilTheFileIsSavedAgain` | No | `SeededProject` | keep |
| Through the real client, activation after a real save reads the save and the changes again and starts nothing | I | `ActivationRealClientTests.ActivatingAfterAFieldWorksSaveReadsTheSaveAndRunsNothing` | No | `FieldWorksSimulator` | new |
| The top bar, Overview and Review agree on freshness: current, saved since, and applied since | U | `TheTopBarOverviewAndReviewAgreeOnFreshness` | No | fake | F04 |
| The saved-since line names both saves, read with a fixed clock | U | `FreshnessTextTests.TheSavedSinceLineNamesBothSavesWithAFixedClock` | No | fixed clock | new |
| A save after the Baseline says so and reruns nothing; Refresh captures, runs, and says what changed | U | `WorkspacePageTests.ASaveAfterTheBaselineShowsFieldWorksSavedSinceAndNothingReruns`, `.RefreshCapturesABaselineThenAssessesTheSelectionAndSaysWhatChanged`, `.RefreshWithNothingToAssessOnlyCapturesTheBaseline` | No | fake | keep |

- **Existing tests:** the `WorkspacePageTests` freshness family, `ABaselineRefreshThatLeavesAnOlderAssessmentShowingIsNotCurrent`
  and its neighbours.
- **Gap:** no real save reaches the window in any test; the clock is unseamed
  (`src/SIL.Motif.App/ViewModels/HandoffWorkspaceViewModel.cs:271-277`).
- **Missing capability:** none. **Depends on:** F04 (one freshness), F08 (clock, simulator).

#### RUN-04 · P1 · See what changed after a Refresh

*As a linguist, after a Refresh I open "See what changed" and see which words moved between cells, the ones
that got worse first.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| "See what changed" appears after a run that moved words, and opens What changed | U | `WorkspacePageTests.ARunOpensTheMatrixAndARerunWithMovesOpensWhatChanged` | No | fake | keep |
| Moves are named, grouped, worse ones first; a timeout is never a verdict; choosing a move outlines both cells | U | `DifferenceViewModelTests` | No | fake | keep |
| Through the real client, What changed compares the new run with the one before it, not another | I | `TextsRealClientTests.WhatChangedComparesTheRunWithTheOneBeforeIt` | No | `SeededEvidence` (two runs) | new |
| Two runs with no words in common say there was nothing to compare, and the panel stays visible | U | `DifferencePanelTests.TwoRunsWithNoSharedWordsSayThereWasNothingToCompare` | No | fake | new |

- **Existing tests:** `DifferenceViewModelTests.ARerunIsComparedWithTheRunItFoldedInto`.
- **Gap:** the no-shared-words case is invisible today (bug 6).
- **Missing capability:** the no-shared-words message (§5).

#### RUN-05 · P1 · Tell unfinished words from words that failed

*As a linguist, a word the parser did not finish, because of the step limit or the per-word time limit, is
shown as unfinished, never as "no analysis"; with a higher limit it can finish.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| A capped word stays incomplete even with readings; a timeout is not "no analysis" | I | `AssessCommandTests.PartialFindingsRemainIncompleteInTheResponseAndStoredWord`, `.AWordWithReadingsThatAlsoHitALimitCountsAsIncomplete`, `.TimingOverlaysRerunRowsAndLabelsTimeoutWithoutMorphology` | No | fake assessor | keep |
| A restored timed-out word stays incomplete | U | `WorkspaceContextTests.RestoredTimedOutWordRemainsIncomplete` | No | fake | keep |
| A per-word timeout is a word outcome; a hung process is stopped by the wall-clock cap | U | `PanGlossInvokerTests.TheWallClockCapStopsAHungParser_AndReportsTimedOut` | No | `FakePanGloss` hang | keep |
| The completion wording is the same on every page and in the CLI | U | the completion-text table test | No | none | F09 |
| With the real parser, a step-capped word finishes under a higher step limit | I | `RealParserLimitTests.AStepCappedWordFinishesUnderAHigherStepLimit` | Yes | `Conformance` | moves |

- **Existing tests:** as in the table.
- **Gap:** the real-grammar limit check exists only inside the 660-second window walkthrough.
- **Missing capability:** none. The per-word time limit stays (owner, 2026-09-24).

#### RUN-06 · P1 · Rerun chosen words with new limits

*As a linguist, I pick the slow or unfinished words and rerun only them with more time or steps; the rest of
the result stays as it was.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| A Timing rerun sends exactly the chosen words with the new time and step limits | U | `WorkspaceContextTests.TimingRerunReportsEachWordAndPassesTimeAndStepLimitsThroughSelection` | No | fake | keep |
| New results replace only the rerun words; the rest are unchanged | U | `AssessViewModelTests.RerunningAnotherWordAddsItToTheWordsAlreadyMeasured` | No | fake | keep |
| Through the real client, a rerun of chosen words stores their new rows and Timing shows them | I | `TimingRealClientTests.ARerunOfChosenWordsReplacesOnlyThoseWords` | No | `SeededEvidence`, `FakePanGloss` | new |
| Retry-failed and retry-slower-than take words from exactly the named run, strictly above the threshold | I | `SelectionComposerTests.RetryFailedSourceContributesTheNewestRunsNoAnalysisAndSkippedWords`, `.RetrySlowerThanSourceContributesWordsStrictlyAboveTheThreshold` | No | `SeededProject` | keep |

- **Existing tests:** `WorkspaceContextTests.RerunRefreshesTheSelectedTimingFromTheStoredOverrides`.
- **Gap:** no real-client rerun. **Missing capability:** none.

#### RUN-07 · P0 · The parser is missing, or is the wrong one

*As a linguist without a working PanGloss, I am told plainly that runs cannot happen, and everything else in
the window still works.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| An absent parser, one that will not start, or one that describes itself wrongly is "unavailable", before any batch | U | `PanGlossInvokerTests.AnAbsentExecutableIsUnavailable_NotAnException`, `.AnExecutableThatWillNotStartIsUnavailable_NotAnException`, `.AWrongDescriptionRefusesBeforeTheFirstBatch`, `PanGlossExecutableTests` | No | `FakePanGloss` sentinels | keep |
| A run that could not start its parser is refused as parser-unavailable and stores nothing | I | `AssessCommandTests.AnAssessorThatCouldNotRunItsParser_IsRefusedAsParserUnavailable_AndRecordsNothing` | No | fake assessor | keep |
| With no parser, a run through the real client is refused with a reason, and Overview, Texts and Review changes still load | I | `ParserAbsentRealClientTests.WithNoParserARunIsRefusedAndEveryOtherPageStillLoads` | No | no parser | new |
| Try a Word says the parser is missing rather than showing a parse | I | `WordTraceQueryTests.AParserThatIsAbsentIsATypedRefusal_NotAnException` | No | no parser | keep |
| The refusal reads as a window sentence, with the details folded away | U | `WindowRefusalTests` | No | none | F06 |

- **Existing tests:** as in the table.
- **Gap:** nothing checks that the rest of the window survives an absent parser.
- **Missing capability:** a refusal that says where Motif looked and what to install (§5).

#### RUN-08 · P1 · A broken parser answer is never shown as a result

*As a linguist, if PanGloss crashes or writes rubbish, I see that the run failed, not a page of "no
analysis".*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| A zero exit with no output is incomplete | U | `PanGlossInvokerTests.Batch_AZeroExitThatWroteNoCacheIsIncomplete` | No | `FakePanGloss` noReport | keep |
| A malformed batch row is refused, never read as "no analysis" | U | `PanGlossBatchFailureTests.AMalformedBatchRowIsRefusedNotReadAsNoAnalysis` | No | `FakePanGloss` malformedBatch | new |
| A crash part-way through a batch is refused and leaves no scratch folder | U | `PanGlossBatchFailureTests.ACrashPartWayThroughABatchIsRefusedAndLeavesNoScratch` | No | `FakePanGloss` crashAfterWords | new |
| After each of those, no Assessment is stored | I | `AssessParserFailureTests.ABrokenParserAnswerStoresNoAssessment` | No | `SeededProject`, `FakePanGloss` | new |

- **Existing tests:** `PanGlossInvokerTests.Stats_ANonZeroExitIsRefusedWithItsStandardError`.
- **Gap:** malformed and crashed batches. **Missing capability:** two fake-parser behaviours (§4.1).

#### RUN-09 · P1 · See how far a long run has got

*As a linguist waiting on a long run, I see how many words are done and which word is being parsed.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| The completed count and the current word travel from the parser boundary through the run command | I | `AssessProgressTests.ARunReportsEachFinishedWordAndTheWordInProgress` | No | `FakePanGloss` streamProgress, delay | new |
| The progress line names the count and the current word, and invents no count after a cancel | U | `RunProgressTextTests.ProgressNamesTheCountAndTheCurrentWord` | No | fake | new |

- **Existing tests:** `AssessCommandTests.ProgressReportsOnlyTheCommandOwnedStagesWithNoPerWordTick` pins the
  opposite today, and changes if the owner agrees (§8 question 7).
- **Gap:** the run reports stages only, although the parser boundary already reads each finished word
  (`src/SIL.Motif.Host/PanGloss/PanGlossInvoker.cs:290`) and Review's check shows "N of M words checked".
- **Missing capability:** per-word progress for a run (§5).

#### RUN-10 · P2 · Two runs at once

*As a linguist with two projects, or with an agent working beside me, two runs never swap their results or
cancel each other.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Two clients on two projects both finish, each with its own words and ids; cancelling one leaves the other | I | `ConcurrentRunTests.TwoClientsOnTwoProjectsEachKeepTheirOwnResult` | No | `TwoProjects`, `FakePanGloss` delay | moves |
| A window run and a CLI `assess` on two projects do not cross results | I | `ConcurrentRunTests.AWindowRunAndACliRunOnTwoProjectsDoNotCrossResults` | No | `TwoProjects`, `FakePanGloss` | new |
| The machine admits at most its parser slots | U | `MachinePanGlossQueueTests` | No | none | keep |

- **Existing tests:** `ConcurrentWalkthroughTests.TwoWindowsShareTheManagedRootWhileBothAssessmentsComplete`
  (real parser, one process).
- **Gap:** no cross-process check. **Missing capability:** none.

#### RUN-11 · P1 · The real PanGloss works with Motif

*As a linguist with PanGloss installed, it is found, it understands the project, and its results are
stored correctly.* This is the compatibility lane, the only place the real parser runs.

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| A batch over the seeded project gives readings whose morphemes resolve | I | `RealParserBatchTests`, `AssessCommandTests` (its real-parser case) | Yes | `SeededProject` | keep |
| The conformance grammar gives its known analyses through the run command | I | `ConformanceGrammarAssessTests.TheConformanceGrammarGivesItsKnownAnalyses` | Yes | `Conformance` | moves |
| A trace explains a failed word | I | `RealParserTraceTests` | Yes | `SeededProject` | keep |
| An AI Handoff from a real run has its five files, and they read back | I | `RealParserHandoffTests.AHandoffFromARealRunHasItsFiveFiles` | Yes | `SeededProject` | moves |

- **Existing tests:** `ConformanceGrammarWalkthroughTests.RealWindowWalksConformanceGrammarFromBaselineToHandoff`
  (660 seconds), `AssessmentWalkthroughTests`, `HandoffWalkthroughTests`.
- **Gap:** eleven real-parser tests skip because PanGloss v0.3.3 has no `assess` subcommand, and every
  real-parser test skips in a worktree (deferred questions). A skip is silent.
- **Missing capability:** a CI step that runs this lane and prints its skips (§8 question 2).

### 2.3 Read the results

#### READ-01 · P0 · See where the project stands

*As a linguist, Overview shows the stored numbers at once, says when they are stale, and each tile takes me
to the words behind it without rerunning anything.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Overview is reachable and shows a result after the first run | S | (smoke) `FirstProjectSmokeTests` | No | `FakePanGloss` | new |
| No run yet shows no zeroes; stale numbers are marked; an unresolved Default Selection is explained | U | `OverviewPageModelTests.WithoutAStoredRunNoResultIsShownAsZero`, `.StaleNumbersAreMarked`, `.AnUnresolvedDefaultSelectionIsExplained` | No | fake | moves |
| Each tile opens its page, filtered, without starting a run | U | `MainWindowSmokeTests.OverviewLoadsStoredNumbersThroughThePageContextAndItsTilesNavigateWithoutRunningAnAssessment` (trimmed to the tile clicks) | No | fake | keep |
| Opening a project shows its stored Overview through the real client, and no parser starts | I | `OverviewRealClientTests.OpeningAProjectShowsItsStoredNumbersAndStartsNoParser` | No | `SeededEvidence` | new |
| Overview and the Matrix classify the same stored words the same way | I | `CompareOverviewParityTests.AppMatrixAndOverviewClassifyTheSameStoredAssessmentWords` | No | `SeededEvidence` | keep |

- **Existing tests:** `WorkspaceContextTests.OpeningAProjectReadsItsStoredOverviewThroughThePageContext` (fake).
- **Gap:** the numbers and wording are asserted in a window over a fake (§3).
- **Missing capability:** none. **Depends on:** F04.

#### READ-02 · P0 · Find the words to work on

*As a linguist, the Matrix shows my words by what the parser found and what the project holds, I can count
words or occurrences, search, and "Fix these first" lists the most useful words first.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Words versus occurrences; search narrows the list, never the Matrix; Fix these first ranks by rank, then occurrences, then form, leaving unknown ranks out; focusing picks the exact form | U | `CompareViewModelTests.TheMatrixCanCountOccurrencesWithoutChangingTheWordList`, `.SearchingNarrowsTheListButNeverTheMatrix`, `.FixFirstRanksTheNamedProblemsByFrequencyAndLeavesUnknownOut`, `.FocusingAFixFirstWordMatchesTheExactForm` | No | fake | keep |
| Clicking a cell lists only that cell's words | U | `CompareViewModelTests.ClickingACellListsOnlyItsWords` | No | fake | moves |
| Through the real word list, Fix these first is not empty when the project has approved analyses the parser missed | I | `TextsRealClientTests.FixTheseFirstListsTheSeededMissesFromTheRealWordList` | No | `SeededEvidence` | new |
| The Text word list has distinct words, their occurrences and the approved analysis | I | `TextWordsQueryTests.TheSeededTextsWordsAreDistinctWithOccurrencesAndTheApprovedAnalysis` | No | `SeededProject` | keep |
| The word list reads the store, not the project file | I | `TextWordsReadNoProjectFile` | No | `SeededProject` | F14 |
| Three quick changes of the chosen Texts complete one query, for the last choice | U | the three-quick-changes test | No | fake | F14 |

- **Existing tests:** `MainWindowSmokeTests.ClickingACompareCellListsOnlyItsWords`,
  `.TextsMatrixShowsAssessmentWordsWithoutIdentifiers`, `.FixFirstCollapsesToItsCountAndHighlightsTheFocusedWordWithItsReasonVisible`.
- **Gap:** the owner recorded that Fix these first was always empty while fake-fed tests passed; no
  real-query test guards it.
- **Missing capability:** none. Fix these first stays below the Matrix until the owner decides; no test
  pins its placement.

#### READ-03 · P1 · Read a Text in context

*As a linguist, Analyze texts shows each word of a Text with what is stored for it and what the parser
found, and I can filter to the differences without losing my place.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Each occurrence is judged against what is stored there; filtering keeps only lines with differences and dims the rest; an opinion needs an explicit reading | U | `ResultsInTextViewModelTests.EachOccurrenceIsJudgedAgainstWhatIsStoredThere`, `.FilteringToDifferencesKeepsOnlyTheirLines_AndDimsTheOtherWordsThere`, `.OpinionsNeedAnExplicitReadingAndMarkEveryOccurrenceOfTheWordform` | No | fake | keep |
| The word list is most frequent first, with status filters | U | `TextWordsViewModelTests.WordsAreListedMostFrequentFirstWithTheLatestAssessmentsResult`, `.TheStatusFilterShowsOnlyMatchingRows` | No | fake | keep |
| The reader's lines and tokens come from the real inventory of the chosen Text | I | `TextsRealClientTests.AnalyzeTextsShowsTheChosenTextsRealTokens` | No | `SeededEvidence` | new |
| A rejected analysis has the same label in the Matrix list, Analyze texts, Try a Word and Review changes | U | the one-label view test | No | fake | F09 |

- **Existing tests:** `MainWindowSmokeTests.AnalyzeTextsSwitchesBetweenTheReaderAndWordList` (fake data).
- **Gap:** no real-inventory test. The unused older reader is deleted by F12.
- **Missing capability:** none.

#### READ-04 · P1 · Answer a named question with Lists

*As a linguist, I choose a question such as "approved but not found" and get exactly those words.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Each list selects only its defined cells; choosing a list clears the Fix these first search | U | `TextsListsViewModelTests.EachNamedListSelectsOnlyItsDefinedMatrixCells`, `.ChoosingAListClearsTheFixFirstSearchAndShowsItsWholeCell` | No | fake | keep |
| From real stored evidence, each list holds the words its question names | I | `TextsRealClientTests.EachListHoldsTheWordsItsQuestionNames` | No | `SeededEvidence` | new |

- **Existing tests:** `TextsListsViewModelTests.OpeningListsAfterFixFirstRestoresEveryWordInTheSelectedCell`.
- **Gap:** no real-evidence test. **Missing capability:** none.

#### READ-05 · P1 · Find where the time went

*As a linguist, Timing shows which kinds of rule and which rules cost the most, and the words that made them
costly, from what the run stored.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Word sets decode; aggregates group by kind and rule, ordered; the costliest five words show but every word under a rule can be handed off | U | `CatalogAggregationTests`, `WorkspaceContextTests.TimingPageShowsTheCommandsKindAndRuleAggregatesUnchanged`, `.TimingShowsFiveCostliestWordsButHandsOffEveryWordUnderTheRule` | No | fake | keep |
| Timing and `stats` read exactly the named run's rows and overrides, never "the latest"; tampered evidence is refused | I | `StatsCommandTests` | No | `SeededEvidence` | keep |
| Opening Timing through the real client reads stored rows and starts no parser | I | `TimingRealClientTests.OpeningTimingReadsTheStoredRowsAndStartsNoParser` | No | `SeededEvidence` | new |
| No Timing action reaches PanGloss; kind, rule and word views still show | U | the Timing-without-statistics test | No | fake | F13 |

- **Existing tests:** `WorkspaceContextTests.TimingWordSetsUseTheStoredCommandWithoutStartingAParser` (fake).
- **Gap:** no real-client open. **Missing capability:** none; F13 settles the Passes column.

#### READ-06 · P1 · Try a Word

*As a linguist, I type any word, even one in no Text, and see the parser's best path, where it failed, and
the rules it spent time in.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| A typed word is tried without Text data; its rule rows use only stored per-rule timing | U | `TryWordPageTests.ATypedWordCanBeTriedWithoutTextDataAndItsRuleRowsUseOnlyStoredPerRuleTiming` | No | fake | keep |
| Success, failure, timeout, no Baseline and absent parser each come back typed | I | `WordTraceQueryTests` | No | fake invoker | keep |
| Through the real client, a word in no Text is traced and its trace reaches the page | I | `TryWordRealClientTests.AWordInNoTextIsTracedThroughTheRealClient` | No | `SeededProject`, `FakePanGloss` trace | new |
| An invalid saved diagnostic gives the same error from Try a Word as from the diagnostic window | U | `DiagnosticOpeningTests` | No | fake | F12 |
| Opening and saving a diagnostic, and copying, run through scripted seams | U | the desktop-seam walkthrough | No | scripted | F17 |

- **Existing tests:** `TraceWordViewModelTests`, `WorkspaceContextTests.OpeningATypedAssessedWordThroughTheContextSelectsItsReadingDetail`.
- **Gap:** no real-client trace. **Missing capability:** none.

#### READ-07 · P1 · Read the grammar warnings, and check on demand

*As a linguist, Warnings shows the last stored check or "Not checked yet", and checks only when I ask.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Opening a project never checks the grammar and offers the check | U | `WorkspacePageTests.OpeningAProjectWithNothingStoredNeverChecksTheGrammarAndOffersTheCheck`, `WorkflowShellTests.AProjectWithNoStoredGrammarCheckOffersAWorkingCheckButtonOnWarnings` | No | fake | keep |
| Findings group, count, filter; an unavailable FieldWorks link explains why | U | `GrammarWarningsViewModelTests`, `GrammarFindingGroupsTests`, `MainWindowSmokeTests.UnavailableGrammarWarningLinksExplainWhyFieldWorksCannotOpenTheSubject` | No | fake | keep |
| Stored results and filters read back through the command | I | `WarningsCommandTests`, `GrammarCheckQueryTests` | No | `SeededProject` | keep |
| Through the real client, a check stores its result and Overview's Warnings tile then shows it | I | `WarningsRealClientTests.CheckingTheGrammarStoresTheResultOverviewThenShows` | No | `FakePanGloss` grammar-health | new |
| `grammar check`, then `warnings --json`, has the check with the same count | I | the CLI grammar-check test | No | `FakePanGloss` | F07 |

- **Existing tests:** `WarningsArgvTests`, `StoredGrammarCheckQueryTests`.
- **Gap:** no real-client check. **Missing capability:** none after F07.

#### READ-08 · P1 · Give words to an AI (AI Handoff)

*As a linguist, I send the whole result, a list, or the words I ticked to AI Handoff, choose an empty
folder, and get five files and a starter prompt I can drag and paste.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Each source sends the right words: a whole list, only ticked words, a Timing rule's words | U | `WorkspaceContextTests.ListsCanHandOffEveryWordInTheSelectedList`, `.ListsCanHandOffOnlyTheTickedWordsInTheSelectedList`, `.AnalyzeTextsCanHandOffOnlyTheTickedWords` | No | fake | keep |
| Ticked words are measured and handed off without a prior run, and only those words are in the files | I | `HandoffSelectedWordsTests.TickedWordsAreMeasuredAndHandedOffWithoutAPriorRun` | No | `SeededProject`, fake assessor | new |
| The writer publishes five files only when finished, refuses a folder that is not empty, and cleans up after a cancel | I | `HandoffWriterTests` | No | `SeededProject` | keep |
| Through the real client, handing off ticked words writes the files and lists them for dragging | I | `HandoffRealClientTests.HandingOffTickedWordsWritesAndListsTheFiles` | No | `SeededEvidence`, `FakePanGloss` | new |
| Cancelling publishes nothing, and a retry succeeds | I | `HandoffRealClientTests.CancellingPublishesNothingAndARetrySucceeds` | No | `FakePanGloss` hold | moves |
| The written files survive a flat upload | I | `HandoffRealClientTests.TheWrittenFilesSurviveAFlatUpload` | No | `FakeChatReceiver` | moves |
| Files are listed, drag hands over exact paths, and the starter prompt can be copied | U | `HandoffViewModelTests.RunningWritesTheDestinationAndExposesEveryFileAsADraggableRow`, `.DragAllFilesAsyncHandsTheDragAdapterEveryFilesPath`, `.DragFileAsyncHandsTheDragAdapterExactlyThatFilesPath` | No | fake | keep |

- **Existing tests:** `HandoffWalkthroughTests`, `CancelHandoffWalkthroughTests`,
  `UploadSimulationWalkthroughTests` (all real parser, whole window); `FakeChatReceiverTests`;
  `MainWindowSmokeTests.ListsShowSeparateAiHandoffsForTheListAndItsTickedWords` (buttons only).
- **Gap:** handing off chosen words is always refused today (bug 5); no test sends such a request to the
  real command.
- **Missing capability:** none once bug 5 is fixed.

### 2.4 Collect changes, review them, and apply them to FieldWorks

#### APPLY-01 · P0 · Collect changes while reading results

*As a linguist, I add many parser readings as Candidates, or mark many words as Incorrect spelling, in one
go; but I approve, reject or send back to Candidate one word and one reading at a time.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| One change collected in Texts reaches Review changes | S | (smoke) `ChangeToReceiptSmokeTests` | No | `SeededEvidence` | new |
| Bulk offers Add as candidate and Incorrect spelling only; Approve, Reject and Back to candidate need one word and one chosen reading | U | `CompareActionsTests.MatrixAllowsBulkChangesAndOneExplicitOpinion`, `.ACheckedWordOpinionUsesItsOneChosenAnalysis`, `.OpinionActionsRefuseSeveralCheckedWordsEvenWhenEachHasASelectedReading`, `CompareViewModelTests.MatrixOffersOnlyChangesAllowedForBulkSelection` | No | fake | keep |
| Through the real client, each kind of change is stored against its exact wordform or stored analysis | I | `CollectRealClientTests.EachKindOfChangeIsStoredAgainstItsExactWordformOrAnalysis` | No | `SeededEvidence` | new |
| A change made on one occurrence in a Text targets the wordform everywhere | I | `CollectRealClientTests.AChangeMadeOnOneOccurrenceTargetsTheWordformEverywhere` | No | `SeededEvidence` | new |
| A later choice for the same reading replaces the earlier one and bumps the revision | I | `PendingChangesTests.LaterChoiceForSameStoredReadingReplacesEarlierChoiceAndBumpsRevision` | No | `SeededProject` | keep |
| The replacement notice reaches the window through the real client | I | the F16 replacement test in `PendingChangesCliAppTests` | No | `SeededProject` | F16 |
| A second change for the same slot is refused and the first is kept | I | `PendingChangesTests.ASecondChangeForTheSameSlotIsRefusedWithoutLosingTheFirst`, `AnalysisChangeSlotTests` | No | `SeededProject` | keep |
| Back to candidate on a reading the project does not store writes nothing | I | `CollectRealClientTests.BackToCandidateOnAReadingTheProjectDoesNotStoreWritesNothing` | No | `SeededEvidence` | new |
| A bulk action skips a word with an explicit choice and says so | U | `PendingChangesViewModelTests.CollectionReportsReplacementsAndSkippedBulkWords` | No | fake | keep |

- **Existing tests:** `CompareActionsTests.AnAddedCandidateCanBeAppliedWithTheOtherAnalysisChanges`,
  `PendingChangesViewModelTests.BulkCandidatesSendEveryParserReadingWithItsAssessmentIndex` (fake).
- **Gap:** the fake client never refuses a same-slot change and never reports a replacement (F16), so no
  page test proves what production does.
- **Missing capability:** none. The owner's bulk-versus-individual ruling stands; the extension to Reject
  and Back to candidate is §8 question 6.

#### APPLY-02 · P0 · Add the expected analysis from Try a Word

*As a linguist, when Try a Word shows the analysis I expected is stored but not approved, one button queues
exactly that analysis for approval.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| One stored Candidate or Rejected expected analysis enables the button; approved or not stored disables it with a reason | U | `TryWordPageTests.AStoredExpectedCandidateCanBeApprovedByItsStoredIdentity`, `.AnApprovedExpectedAnalysisStaysDisabledWithAnOnScreenReason`, `.AnExpectedAnalysisWithoutAStoredIdentityStaysDisabledWithAnOnScreenReason` | No | fake | keep |
| Several stored Candidates and no approved one leave the button disabled with a reason | U | `ExpectedAnalysisTests.SeveralStoredCandidatesLeaveTheButtonDisabledWithAReason` | No | fake | new |
| Through the real client, the stored analysis is queued by its identity, never a parser reading, and Apply approves exactly it | I | `CollectRealClientTests.TheExpectedAnalysisIsQueuedByItsStoredIdentityAndApplyApprovesIt` | No | `SeededEvidence` | new |

- **Existing tests:** `PendingChangesViewModelTests.ApprovingAStoredAnalysisUsesItsIdentityWithoutAParserReading`.
- **Gap:** no real-cache approval. **Missing capability:** none.

#### APPLY-03 · P1 · See what is pending everywhere, and remove one

*As a linguist, every page marks the words I have changes for, the sidebar says how many are not applied
yet, and removing one leaves the others.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| One shared list: the Matrix, Lists and Review changes show the same changes and count; "no longer fits" outranks a plain pending mark | U | `WorkflowShellTests.TheMatrixAndTheReviewPageShowTheSameChangesList`, `WorkspacePageTests.TheReviewBadgeCountsTheOneChangesListTheMatrixAddsTo`, `CompareViewModelTests.AStaleWordTakesPriorityInItsCellPendingStatus`, `TextsListsViewModelTests.APendingChangeIsSharedWithTheNamedListAndTheMatrixSelection` | No | fake | keep |
| Removing one change through the real client keeps the others, and a reload shows the same | I | `ReviewRealClientTests.RemovingOneChangeKeepsTheOthersAcrossAReload` | No | `PendingChange` ×2 | new |
| A removal against an old revision is refused, the list reloads, and the refusal stays visible | U | `PendingChangesViewModelTests.RevisionConflictReloadsTheDraftAndKeepsTheRefusalVisible` | No | fake | keep |

- **Existing tests:** `WorkspaceContextTests.AnAnalyzeChangeShowsNotAppliedYetInTheMatrixAndItsList`.
- **Gap:** an ordinary successful removal is not tested against the store. **Missing capability:** none.

#### APPLY-04 · P0 · See what applying would do

*As a linguist, Review changes shows each change and whether it still fits, and "Check these changes"
measures only the words they touch and says what applying would do to the numbers.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Check these changes shows progress and then the numbers | S | (smoke) `ChangeToReceiptSmokeTests` | No | `FakePanGloss` | new |
| Opening Review starts nothing; Apply is enabled only after complete numbers for this exact set of changes; cancelling leaves it disabled | U | `ReviewPageModelTests.OpeningReviewDoesNotStartAParserRun`, `.ApplyEnablesOnlyAfterThePersonMeasuresCompleteEvidence` | No | fake | keep |
| The check measures only the distinct changed words | I | `PendingChangesTests.DefaultTrialQueuesOnlyTheTouchedWord` | No | `SeededProject` | keep |
| Measuring runs as one command with progress; cancelling cancels the job and keeps the changes | I | the `PendingChangesWorkflow` measure and cancel tests | No | `SeededProject`, `FakePanGloss` | F02 |
| The numbers are a typed record; the window never says "Assessment", "Assessor" or "regression" | U | the `ReviewPageModel` wording test; the four `Comparability` cases | No | fake | F03 |
| Through the real client, Check these changes shows numbers for the current set | I | `ReviewRealClientTests.CheckingTheChangesShowsTheirNumbersThroughTheRealClient` | No | `PendingChange`, `FakePanGloss` | new |

- **Existing tests:** `ReviewCommandClientTests.ASeededTrialFlowsThroughReviewApplyWithoutRewritingTheDraft`
  (moved by F02).
- **Gap:** the Review page is never driven over the real client. **Missing capability:** none.
- **Depends on:** F02, F03.

#### APPLY-05 · P0 · Apply to FieldWorks project, and see the Receipt

*As a linguist, I press Apply to FieldWorks project; the button is busy until it finishes, then says
"Applied to the FieldWorks project", and the change is really in my project.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Apply → the Receipt shows and the badge clears | S | (smoke) `ChangeToReceiptSmokeTests` | No | `PendingChange` | new |
| Apply writes the changes, records one Receipt, and leaves no changes pending; stale revisions and unready evidence reopen the changes | I | the moved Review workflow test and `ARefusalAfterFinalizeReopensTheDraft` | No | `SeededProject`, `FakePanGloss` | F02 |
| An Apply queued behind another call can be cancelled with a typed refusal | I | the gate-cancellation test that replaces `ApplyWaitsForTheProjectReadGate` | No | `SeededProject` | F02 |
| A fresh load of the project after Apply has exactly the chosen analysis state | I | `ApplyReadBackTests.AFreshLoadOfTheAppliedProjectHasExactlyTheChosenState` | No | `PendingChange`, `FieldWorksSimulator` | new |
| A failure part-way rolls every change back and writes no applied-log entry | I | `ProposalApplierTests.Apply_MidProposalFailure_RollsBack_AndWritesNoAppliedLogEntry`, `.Apply_UnknownTarget_ThrowsAndRollsBack_AndWritesNoAppliedLogEntry` | No | `SeededProject` | keep |
| A failure after the write is reported as "check the project", never as a rollback, and is not retried | I | `ProposalWorkflowTests.Apply_ManifestWriteFails_AfterAGenuineCommitAndSave_ReportsReconciliation_NotRollback` | No | `SeededProject` | keep |
| The window says that same failure differently from an ordinary refusal | U | `ApplyOutcomeTextTests.AFailureAfterWritingIsNotCalledAnUndoneApply` | No | fake | new |
| Apply cannot start twice while it runs | U | `ApplyOutcomeTextTests.ApplyCannotStartTwiceWhileItRuns` | No | fake | new |
| The Receipt shows and the list empties | U | `ReviewPageModelTests.ApplyingMeasuredChangesShowsTheReceiptAndEmptiesTheList` | No | fake | keep |

- **Existing tests:** `AuthorLexemeFormEndToEndTests`, `ProposalWorkflowTests`.
- **Gap:** no window-to-real-Apply path; no read-back through a fresh load after an App Apply.
- **Missing capability:** none. **Depends on:** F02, F08.

#### APPLY-06 · P0 · A change no longer fits, because FieldWorks moved on

*As a linguist, if FieldWorks deleted or changed what one of my changes was about, Review changes says that
change "no longer fits" and Apply waits; I remove it or check again, and the others go ahead.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| A save that deletes a change's word shows "no longer fits" before any Refresh | I | `AFieldWorksSaveThatDeletesTheTargetShowsNoLongerFitsBeforeRefresh` | No | `SeededProject` | F01 |
| Check again renews only changes that still fit; a deleted word, a changed spelling status or a changed decision is not renewed; an unrelated change is | I | `PendingChangesTests.CheckingAgainDoesNotRenewADeletedWordform`, `.CheckingAgainDoesNotRenewAChangedSpellingStatus`, `.CheckingAgainDoesNotRenewAChangedAnalysisDecision`, `.CheckingAgainRefreshesTheFingerprintWhenAnUnrelatedWordChanged` | No | `SeededProject` | keep |
| A change that no longer fits blocks Apply even with `--force` | I | `ProposalWorkflowTests.DeletedWordform_IsNoLongerFitAndBlocksApplyEvenWithForce`, `.MissingChangeFingerprintBlocksApplyEvenWithForce`, `ProposalApplierTests.Apply_AfterFootprintMovedSinceDryRun_IsADriftHardStop` | No | `SeededProject` | keep |
| Each row says whether it fits; Apply is blocked; removing the ones that no longer fit keeps the rest | U | `ReviewPageModelTests.ADeletedWordformBlocksApplyAndRemovingItKeepsTheOtherChange`, `.CheckAgainReplacesOnlyTheFitsThatPassedTheCommand`, `.AFieldWorksSaveBlocksApplyUntilRefresh`, `PendingChangesViewModelTests.AStaleChangeIsVisibleAndBlocksReview` | No | fake | keep |
| Through the real client, after FieldWorks deletes one change's word, that change no longer fits, can be removed alone, and the other can be checked and applied | I | `ReviewRealClientTests.AChangeWhoseWordFieldWorksDeletedCanBeRemovedAlone` | No | `PendingChange` ×2, `FieldWorksSimulator` | new |
| An unrelated save asks for a check again, keeps the change, and needs new numbers before Apply | I | `ReviewRealClientTests.AnUnrelatedSaveNeedsCheckingAgainButKeepsTheChange` | No | `PendingChange`, `FieldWorksSimulator` | new |
| A save between checking and applying refuses the Apply, writes nothing and keeps the changes | I | `ReviewRealClientTests.ASaveBetweenCheckingAndApplyingIsRefusedAndKeepsTheChanges` | No | `PendingChange`, `FieldWorksSimulator` | new |

- **Existing tests:** `ReviewPageModelTests.RefreshReloadsChangeFitBeforeApply`.
- **Gap:** the page rules are proven over a fake only; a real FieldWorks save never reaches Review.
- **Missing capability:** none. **Depends on:** F01, F08.

#### APPLY-07 · P0 · FieldWorks has the project open

*As a linguist who keeps FieldWorks open, Motif still lets me read results and collect changes, tells me to
close the project before applying, and applies once I have.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| With the project held, Review changes says why Apply is blocked; after release it can proceed | S | (smoke) `FieldWorksBesideMotifSmokeTests` | No | `FieldWorksSimulator` | new |
| Reading and collecting changes succeed while FieldWorks holds the project; an unchanged project is not opened at all; Apply still refuses | I | `PendingChangesWithFieldWorksOpenTests.LoadAndPutSucceedWhileFieldWorksHoldsTheProject`, `.LoadWithAnUnchangedProjectReadsNoProjectFile`, `.ApplyStillRefusesWhileHeld` | No | held lock | F01 |
| Activating the window with pending changes while held does not fault | I | `ActivatingTheWindowWithPendingChangesWhileHeldDoesNotFault` | No | held lock | F01 |
| A held Apply is busy and the changes stay pending for a retry | I | `ProposalWorkflowTests.HeldProject_ApplyIsBusyAndProposalRemainsPendingForRetry` | No | held lock | keep |
| A Baseline can be captured from a held project, which is reported as held | I | `CurrentBaselineQueryTests.ReportsFieldWorksHoldsTheProjectWhenALockFileIsPresentEvenBeforeAnyCapture`, `SavedProjectFileCopierTests` | No | held lock | keep |
| The held wording and the block reason | U | `ReviewPageModelTests.FieldWorksHoldingTheProjectBlocksApplyAndKeepEditingReturnsToTheOrigin`, `WorkspacePageTests.ARefusedRefreshKeepsItsReasonAndFieldWorksHoldStatusVisible` | No | fake | keep |

- **Existing tests:** `RestartAndSwitchWalkthroughTests` writes bytes to a `.lock` file, which the held check
  sees and LibLCM does not, so the window has never met a real lock.
- **Gap:** with one change pending, this is probably a crash today, not a refusal (bug 4). One report
  marked it "works"; F01 corrects that.
- **Missing capability:** none. **Depends on:** F01, F08.

#### APPLY-08 · P1 · Applying would lose something, or the numbers are unfinished

*As a linguist, if checking my changes shows a word would lose an approved analysis, or some words did not
finish, Review changes says so before I press Apply, and Apply waits.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| When the checked numbers show a lost approved analysis, Apply is disabled with that reason, before any click | U | `ReviewLossWarningTests.ALostApprovedAnalysisDisablesApplyWithItsReason` | No | fake | new |
| The loss is judged on the words both measurements share; no shared words says it could not compare | I | `RegressionCheckerTests`, `ReviewNumbersCommandTests` | No | stored measurements | keep |
| The CLI refuses a loss by default, and `--force` applies anyway | I | `ApplyPromotionGatingAndSweepTests.RegressionGated_WithoutAnOverride_RefusesAndNamesTheRegression`, `.RegressionGated_WithForce_AppliesAnyway` | No | `SeededProject` | keep |
| Unfinished words block the window's Apply; the CLI refuses by default; `--force` applies but still refuses a change that no longer fits | I | `ApplyForceArgvTests.ForceOverridesUnfinishedWordsButNeverAChangeThatNoLongerFits` | No | `PendingChange`, `FakePanGloss` capped | new |
| An interrupted word never makes a change ready | U | `ReadinessTests.AllApprovedMatchesDoNotMakeAnInterruptedWordReady` | No | none | keep |
| A refused Apply explains the worse result | U | `ReviewPageModelTests.ARegressionRefusalExplainsTheWorseResult` | No | fake | keep |

- **Existing tests:** `ApplyPromotionGatingAndSweepTests.ApplyWithNoAssessmentAtAll_IsRefused_BecauseNothingHasMeasuredWhatItWouldDo`.
- **Gap:** the window learns of a loss only when Apply is refused (`src/SIL.Motif.App/ViewModels/ReviewPageModel.cs:91-94`
  has no such term).
- **Missing capability:** the warning before Apply (§5). **Depends on:** F03 (its typed record carries the
  counts the warning needs).

#### APPLY-09 · P1 · After Apply: fresh numbers, and the change in FieldWorks

*As a linguist, after applying I see the numbers are now out of date and can refresh them in one step; the
next time I open the project, Motif still shows what was applied; FieldWorks opens the project with the
change in it.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| After Apply the numbers are marked out of date until a Refresh | U | `WorkspacePageTests.ApplyingChangesMakesTheNumbersStaleUntilRefresh` | No | fake | keep |
| A fresh load after Apply and a Motif Refresh agree on the new state | I | `ApplyReadBackTests.AfterApplyAFreshLoadAndARefreshAgreeOnTheNewState` | No | `PendingChange`, `FieldWorksSimulator` | new |
| The Receipt card offers "Refresh the numbers", and it runs the ordinary Refresh | U | `ReceiptNextStepTests.TheReceiptOffersARefreshThatRunsTheOrdinaryRefresh` | No | fake | new |
| Reopening the project shows the last Receipt, with nothing pending | I | `ReviewRealClientTests.ReopeningTheProjectShowsTheLastReceipt` | No | `PendingChange` | new |

- **Existing tests:** `WorkspacePageTests.ApplyingChangesMakesTheNumbersStaleUntilRefresh`.
- **Gap:** the Receipt is kept only in the page model and cleared on reopen
  (`src/SIL.Motif.App/ViewModels/ReviewPageModel.cs:244-253`); nothing reads a real project back after an
  App Apply.
- **Missing capability:** the Refresh step on the Receipt, and the Receipt after reopening (§5). Refresh
  stays a person's act, never automatic (ADR 0046 decision 1).

#### APPLY-10 · P2 · Go back to where I was working

*As a linguist, Keep editing takes me back to the page where I collected the change.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Keep editing returns to the first change's page: Try a Word for one collected there, Texts otherwise | U | `KeepEditingTests.AChangeFromTryAWordReturnsThereAndOneFromTextsReturnsToTexts` | No | fake | new |

- **Existing tests:** `ReviewPageModelTests.FieldWorksHoldingTheProjectBlocksApplyAndKeepEditingReturnsToTheOrigin`
  (fake origin).
- **Gap:** the Texts origin is untested. **Missing capability:** none.

#### APPLY-11 · P2 · Undo an Apply

*As a linguist, I might want to take back what I applied.*

No tests until the owner decides (§8 question 8). Neither the window
(`src/SIL.Motif.App/Views/ReviewPanel.axaml:93-117`) nor the CLI has an undo, and the Receipt records what
was applied without reversing it. Reversing a write needs its own semantics; it must not be simulated by
editing the applied changes.

### 2.5 An AI agent works through the CLI

The CLI tests below run under `./test.ps1`, which turns developer-only verbs on for the whole suite
(`test.ps1:69`). A test that needs the released surface removes `MOTIF_DEVELOPER_COMMANDS` from its child's
environment, as `ReleaseSurfaceTests` does. Today `put-pending-change`, `preflight`, `trial` and `apply` are
developer-only (`tests/SIL.Motif.Tests.Cli/Cli/ReleaseSurfaceTests.cs:30-39`), so an agent on a released
build cannot collect or apply a change at all (§8 question 3).

#### AGENT-01 · P0 · The agent reads where the project stands

*As an agent, `overview`, `timing` and `warnings` give me the same numbers the linguist sees, as JSON.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| `overview --json` has the same counts as the window's Overview for the same stored run | I | `AgentParityArgvTests.OverviewJsonMatchesTheWindowsOverview` | No | `SeededEvidence` | new |
| `warnings` and `stats` exit codes, human text and JSON | I | `WarningsArgvTests`, `StatsArgvTests` | No | `SeededProject`, `FakePanGloss` | keep |
| `grammar check`, then `warnings --json`, shows the check | I | the CLI grammar-check test | No | `FakePanGloss` | F07 |
| Every command-client method that writes the store has a verb | U | the extended catalog parity test | No | none | F07 |
| Response and failure shapes bind | U | `ResponseBindingTests`, `FailureContractTests`, `FailureEnvelopeTests` | No | none | keep |

- **Existing tests:** `CatalogTextRenderingTests`, `CommandCatalogParityTests`.
- **Gap:** no test puts the CLI and the window side by side on one stored run.
- **Missing capability:** `grammar check` (F07).

#### AGENT-02 · P0 · The agent measures a project

*As an agent, I capture a Baseline and run an Assessment from the command line, read the result as JSON,
and can stop a run cleanly.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| `baseline capture --json` binds to its typed response; an absent project is the usual refusal | I | `BaselineCaptureArgvTests.CapturingAsJsonBindsToTheTypedResponse` | No | `SeededProject` | keep |
| `assess --json` with a stored Default Selection succeeds, prints the Assessment ids, and `timing` and `overview` read them | I | `AssessRoundTripArgvTests.AssessAsJsonStoresARunThatTimingAndOverviewRead` | No | `SeededProject`, `FakePanGloss` | new |
| `assess --words <file>` measures only those words | I | `AssessRoundTripArgvTests.AssessWithAWordsFileMeasuresOnlyThoseWords` | No | `SeededProject`, `FakePanGloss` | new |
| Interrupting `assess` stops its parser, exits as cancelled, and stores nothing | I | `AssessRoundTripArgvTests.InterruptingAssessStopsTheParserAndStoresNothing` | No | `FakePanGloss` hold | new |
| Bad arguments are refused before the project is touched | I | `AssessArgvTests` | No | none | keep |

- **Existing tests:** `AssessArgvTests` covers only refusals; no test runs a successful `assess` through the
  executable.
- **Gap:** a successful round trip, and interruption.
- **Missing capability:** `assess` ignores an interrupt: `Program.cs` passes no cancellation token
  (`src/SIL.Motif.Cli/Program.cs:778-780`; bug 8).

#### AGENT-03 · P0 · The agent collects, checks and applies changes while the window is open

*As an agent, I add a change, check that it fits, and apply it; the linguist's open window then shows the
change as applied, with no duplicate.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Put, list, check again and remove round-trip through the executable, each returning the new revision | I | `AgentChangesArgvTests.PutListRecheckAndRemoveRoundTripThroughTheExecutable` | No | `SeededEvidence` | new |
| Preflight and apply on a finalised Proposal, and their refusals | I | `ProposalWorkflowTests` | No | `SeededProject` | keep |
| `apply --all-pending`, with and without `--revision`, writes one Receipt | I | the CLI all-pending test | No | measured `PendingChange` | F02 |
| `trial --pending --words … --wait` measures the pending changes | I | the CLI pending-trial test | No | `PendingChange`, `FakePanGloss` | F02 |
| While the window is open, an Apply by the CLI is seen when the window is activated: nothing pending, numbers out of date, no duplicate | I | `ActivationRealClientTests.AnApplyByTheCliIsSeenWhenTheWindowIsActivated` | No | `PendingChange` | new |
| Activating the window runs the freshness check | U | `MainWindowActivationTests.ActivatingTheWindowChecksFreshness` | No | fake | new |
| A change put through the CLI appears in the window on opening and after a restart | I | `PendingChangesCliAppTests.CliDraftAppearsInAppAfterOpeningAndAfterRestart` | No | `SeededProject` | keep |

- **Existing tests:** `PendingChangesTests` (command level, in `Tests.Cli`), `RunnerSpineTests`.
- **Gap:** no executable-level test of the pending-change verbs; activation after a CLI write is untested.
- **Missing capability:** `apply --all-pending` (F02); released pending-change verbs (§8 question 3).
  Which stored pages activation should reload is §8 question 4.
- **Depends on:** F02.

#### AGENT-04 · P1 · The agent and the linguist change the same list at once

*As an agent working beside a linguist, if we both change the pending list, neither of us silently
overwrites the other.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| A put with an old revision is refused as a revision conflict; a retry after reading lands once | I | `AgentChangesArgvTests.AStaleRevisionIsRefusedAndARetryLandsOnce` | No | `PendingChange` | new |
| The window's client and a CLI process writing at once: one wins, the other gets a conflict, nothing is lost | I | `PendingChangesConcurrencyTests.TheWindowAndTheCliCannotOverwriteEachOthersChange` | No | `PendingChange` | new |
| The window shows the conflict and reloads | U | `PendingChangesViewModelTests.RevisionConflictReloadsTheDraftAndKeepsTheRefusalVisible` | No | fake | keep |

- **Existing tests:** `PendingChangesCliAppTests` is sequential, not concurrent.
- **Gap:** no two-writer test. **Missing capability:** none.

#### AGENT-05 · P1 · The agent writes an AI Handoff

*As an agent, I write a Handoff from a finished run and get its folder and files, or a typed refusal.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| `handoff` from a retained run writes five files and prints the folder; a folder that is not empty and a missing run are refused, leaving nothing behind | I | `AgentHandoffArgvTests.AHandoffFromARetainedRunWritesFiveFilesAndRefusesAFullFolder` | No | `SeededEvidence`, `FakePanGloss` | new |
| Argument shapes | I | `HandoffArgvTests` | No | none | keep |

- **Existing tests:** `HandoffArgvTests`, `HandoffWriterTests`.
- **Gap:** no successful executable run. **Missing capability:** none.

#### AGENT-06 · P2 · The agent watches and manages jobs

*As an agent, I start a long Trial, watch it, cancel it or queue it again, and a requeue never applies
anything twice.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| `jobs list`, `show`, `cancel`, `requeue` and `move` | I | `JobQueueVerbArgvTests`, `JobVerbArgvTests` | No | `SeededProject` | keep |
| A Trial job runs to completion under a real runner; cancel and requeue keep its history | I | `AgentJobsArgvTests.ATrialJobRunsToCompletionAndCancelAndRequeueKeepItsHistory` | No | `PendingChange`, `FakePanGloss` | new |
| Job states and leases | U | `JobStateMachineTests`, `JobRunnerLoopTests`, `JobLeaseTests` | No | temp store | keep |

- **Existing tests:** as in the table. The window has no job view, and none is proposed.
- **Gap:** no end-to-end Trial job through the executable and a real runner. **Missing capability:** none.

### 2.6 When things go wrong

#### RES-01 · P1 · Close Motif in the middle of a run

*As a linguist, if I close the window during a run, it stops cleanly; when I reopen, I see the last finished
result, never a half-finished one.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Closing waits for running work to stop, and does not block the window while it does | U | `CloseCoordinatorTests.ClosingWaitsForRunningWorkWithoutBlockingTheWindow` | No | held client | moves |
| Closing during a run stores nothing partial, and reopening shows the last finished result | I | `CloseRealClientTests.ClosingDuringARunStoresNothingPartialAndReopenShowsTheLastResult` | No | `SeededEvidence`, `FakePanGloss` hold | new |

- **Existing tests:** `ConformanceGrammarWalkthroughTests.DisposingWithAnAssessmentInFlightCancelsItWithoutBlockingTheDispatcher`
  (real parser).
- **Gap:** closing is not awaited: `Exit` fires `DisposeAsync` and forgets it, and `Closing` only saves the
  window's bounds (`src/SIL.Motif.App/App.axaml.cs:23`, `src/SIL.Motif.App/Views/MainWindow.axaml.cs:35`;
  bug 7).
- **Missing capability:** a close that waits (§5). Killing the App process outright belongs to the native
  lane (§8 question 1).

#### RES-02 · P1 · The job runner stops unexpectedly

*As a linguist or agent, if the background job process dies, its work is picked up again once, never lost
and never done twice.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| A killed runner's job is reclaimed and reaches a final state | I | `RunnerSpineTests.AKilledRunnerLeavesItsJobReclaimableRatherThanStranded` | No | `SeededProject` | keep |
| A runner killed during a Trial: the attempt is marked interrupted, retried once, the published Dry Run is kept, scratch is swept, and no Assessment is duplicated | I | `TrialKillRecoveryTests.KillingTheRunnerDuringATrialRetriesOnceAndKeepsWhatWasPublished` | No | `PendingChange`, `FakePanGloss` hold | new |
| Recovery schedules only an infrastructure retry; startup sweeps an orphaned workspace | U | `WorkerRecoveryTests.StartupMarksRunningAttemptInterruptedAndSchedulesOnlyInfrastructureRetry`, `PanGlossWorkspaceTests.SweepStartup_RemovesAWorkspaceOrphanedByAWorkerCrash` | No | temp store | keep |
| Losing the machine database does not strand the runner | I | `RunnerMachineDatabaseLossTests` | No | temp root | keep |

- **Existing tests:** as in the table.
- **Gap:** the process-level kill covers a Dry Run job, not a Trial. **Missing capability:** none.

#### RES-03 · P1 · Motif's store for this project cannot be used

*As a linguist, if this project's Motif store is from another version, damaged, or cannot be opened, Motif
says so plainly, names the file, and changes nothing.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| A store at another schema is refused, not migrated, with "delete it and let Motif recreate it"; its bytes are unchanged | U | `SchemaVersionGateTests.ADatabaseStampedWithADifferentSchemaIsRefusedRatherThanMigrated`, `MotifDatabaseMigrationTests.NewerSchemaIsRefusedWithoutDowngrade` | No | `OldSchemaStore` | keep |
| Corrupt bytes are refused as invalid data | U | `MotifDatabaseMigrationTests.CorruptDatabaseBytesAreReportedAsInvalidData` | No | `CorruptStore` | keep |
| An unavailable store and an output I/O failure are stable refusals, not "busy" | U | `ProjectStoreCommandTests.AnUnavailableStoreIsAStableRefusalRatherThanBusy`, `.AnActionOutputIoFailureIsAStableRefusalRatherThanProjectBusy` | No | injected failure | keep |
| Through the real client, an old-schema store refuses Refresh on the freshness line with its path, publishes no Baseline, and once deleted is recreated; the `.fwdata` is untouched | I | `StoreRefusalRealClientTests.AnOldSchemaStoreIsRefusedAndOnceDeletedIsRecreated` | No | `OldSchemaStore` | new |
| A corrupt store is refused through the real client and left byte for byte | I | `StoreRefusalRealClientTests.ACorruptStoreIsRefusedAndLeftByteForByte` | No | `CorruptStore` | new |
| The store refusal reads as a window sentence naming the file | U | `WindowRefusalTests` | No | none | F06 |

- **Existing tests:** as in the table.
- **Gap:** the refusal is proven at the store, never where the linguist meets it.
- **Missing capability:** none required; a delete-and-recreate button is §8 question 9.

#### RES-04 · P1 · Two Motif processes use one project at once

*As a linguist with an agent working beside me, two Motif processes can never both apply, or corrupt the
store.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| The window's client and a CLI process applying to one project: one Receipt, and the other gets a typed busy or conflict refusal | I | `SameProjectTwoProcessTests.TwoProcessesApplyingToOneProjectWriteOneReceipt` | No | measured `PendingChange` | new |
| Two agents sharing one Baseline stay consistent | I | `TwoAgentsOneBaselineTests` | No | `SeededProject` | keep |

- **Existing tests:** `ReviewCommandClientTests.ApplyWaitsForTheProjectReadGate` (in-process, by reflection;
  replaced by F02); `RunnerSpineTests` (two different projects).
- **Gap:** no same-project, two-process test. **Missing capability:** none. **Depends on:** F02.

#### RES-05 · P1 · A crash never leaves a Windows error dialog, or a dead window

*As a linguist, if something goes badly wrong, Motif either tells me in the window or exits; it never
hangs behind a hidden crash dialog.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Every executable, the App included, suppresses the crash dialog at startup | U | `EntryPointStartupTests.EveryExecutableSuppressesCrashDialogsAtStartup` | No | none | new |
| A crashing child exits at once with its crash code | I | `CrashDialogsTests.AChildThatCrashesExitsWithTheCrashCodeRatherThanWaitingOnADialog` | No | `FakePanGloss` | keep |
| A failure while opening a project lands on the refusal line and the project menu still works | U | `ProjectOpenFailureTests.AFailureWhileOpeningShowsARefusalAndKeepsThePickerUsable` | No | fake | new |
| A failing Texts reload shows a refusal instead of crashing | U | the Texts-reload test | No | fake | F10 |

- **Existing tests:** `CrashDialogsTests`.
- **Gap:** the App never suppresses the dialog (bug 1); `OnProjectChosen` is `async void` and an exception
  there ends the process (bug 11).
- **Missing capability:** none. **Depends on:** F05 (it wraps `OnProjectChosen`), F10.

#### RES-06 · P1 · A refusal says what went wrong, where it happened

*As a linguist, when Motif refuses something, the message appears beside what I tried, in my words, with
the details one click away.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| Every code the App can meet has a window sentence; none says "Proposal", "Draft", "Preflight" or "motif "; an unknown code keeps its message in Details | U | `WindowRefusalTests` | No | none | F06 |
| The setup dialog shows no command line for the Default Selection refusal | U | the setup refusal view test | No | fake | F06 |
| Refusals appear on their own surface: the Texts page, the Selection panel | U | `MainWindowSmokeTests.TextsPageShowsAssessmentRefusalsAndDetails`, `SelectionViewModelTests.ARefusalWhileLoadingTextsShowsItsMessageAndLeavesTheListEmpty` | No | fake | keep |

- **Existing tests:** as in the table.
- **Gap:** about fifteen sites show the command's own sentence, some with CLI remedies (F06).
- **Missing capability:** the refusal presenter (F06). **Depends on:** F06.

#### RES-07 · P2 · The disk fills up

*As a linguist, a full disk gives a refusal, never a result that looks complete, and no pile of debris.*

| Assertion | Level | Test | Parser | Fixture | State |
|---|---|---|---|---|---|
| A write failure at staging, cache retention, store write or clean-up stores no finished run, and leaves scratch removed or marked for retry | U | `DiskFullTests.AWriteFailureAtAnyStageStoresNoFinishedRun` | No | injected file system | new |
| A locked file at clean-up leaves a retry marker instead of throwing | U | `PanGlossWorkspaceTests.CompleteAndDelete_WithALockedFile_LeavesTheMarkerForARetryInsteadOfThrowing` | No | temp folder | keep |

- **Existing tests:** as in the table.
- **Gap:** no deterministic way to fail a write. **Missing capability:** a file-system seam (§4.6).

### 2.7 Where the reports' ids went

The seven reports each numbered their own scenarios, and several numbers clash, for instance with the
architecture findings F01–F17. This table maps every old id to its workflow here.

| Report | Old id → new id |
|---|---|
| R1 (research) | S-01 → `OPEN-01`; S-02 → `APPLY-05`; S-03 → `RUN-02`; I-01 → `OPEN-05`; I-02 → `RES-03`; I-03 → `APPLY-06`; I-04 → `RES-04`; I-05 → `RUN-02`; I-06 → `RES-02`; I-07 → `RUN-07`; I-08 → `OPEN-14`; I-09 → `RUN-11`; I-10 → `RES-05`; U-01 → `OPEN-13`; N-01 → `OPEN-12` |
| R2 (inventory) | W1 → `OPEN-01`; W2 → `OPEN-02`; W3 → `READ-06`, `READ-07`; W4 → `RUN-01`; W5 → `READ-01`, `READ-05`; W6 → `READ-02`, `APPLY-01`; W7 → `APPLY-04`, `APPLY-06`; W8 → `APPLY-05`; W9 → `READ-08`; W10 → `RUN-02`, `OPEN-05`, `OPEN-06`, `RES-02`; W11 → `AGENT-01`, `AGENT-02`; W12 → `RES-03` |
| R3 (wanted) | A01 → `OPEN-01`; A02 → `OPEN-02`; A03 → `OPEN-03`; A04 → `OPEN-04`; A05 → `OPEN-05`; A06, A07 → `OPEN-06`; A08 → `OPEN-07`; A09 → `RES-03`; A10 → `OPEN-08`; A11 → `OPEN-14`; **B01 → `APPLY-07`**; B02 → `RUN-03`; B03 → `RUN-04`; B04, B05 → `APPLY-06`; B06 → `RUN-02`; B07 → `OPEN-09`; B08 → `OPEN-10`; C01 → `RUN-01`; C02 → `RUN-02`; C03, C04 → `RUN-05`; C05, C06 → `RUN-07`; C07 → `RES-01`; C08 → `RES-02`; C09 → `RUN-10`; D01 → `READ-01`; D02, D03 → `APPLY-02`; D04 → `READ-02`; D05, D06 → `APPLY-01`; D07 → `READ-03`; D08 → `READ-04`; D09 → `READ-05`; D10 → `READ-07`; D11, D12 → `READ-08`; E01 → `APPLY-04`; E02 → `APPLY-05`; E03 → `APPLY-06`; E04 → `APPLY-03`; E05 → `APPLY-08`; E06 → `APPLY-09`; F01 → `AGENT-01`; F02, F03 → `AGENT-03`; F04 → `AGENT-04`; F05 → `AGENT-05`; F06 → `AGENT-06`; G01 → `OPEN-13`; G02 → `OPEN-12` |
| Area A (lifecycle, numbered 1–26) | 1, 2, 3 → `OPEN-01`; 4, 5 → `OPEN-05`; 6 → `APPLY-07`; 7, 8, 9 → `RES-03`; 10, 11 → `OPEN-02`; 12 → `OPEN-04`; 13 → `OPEN-03`; 14 → `OPEN-06`; 15 → `OPEN-07`; 16 → `OPEN-09`; 17 → `OPEN-11`; 18 → `OPEN-10`; 19 → `RUN-03`; 20 → `AGENT-03`; 21 → `RUN-04`; 22 → `RES-01`; 23 → `OPEN-05`; 24 → `OPEN-12`, `OPEN-13`; 25 → `RES-05`; 26 → `RES-06` |
| Area B (jobs) | B01 → `OPEN-01`; B02 → `AGENT-02`; B03, B04 → `OPEN-02`; B05 → `RUN-06`; B06 → `RUN-01`; B07 → `AGENT-02`; B08 → `RUN-09`; B09 → `RUN-02`; B10 → `AGENT-02`; B11, B12 → `RUN-05`; B13, B14 → `READ-05`; B15 → `RUN-06`; B16 → `RUN-02`; B17 → `READ-07`; B18 → `READ-05`; B19 → `RUN-07`; B20 → `RUN-08`; B21 → `RUN-05`; B22 → `AGENT-06`; B23 → `RES-02`; B24 → `RUN-10`; B25 → `RES-01`; B26 → `OPEN-07`; B27 → `RES-07` |
| Area C (analysis, numbered 1–24) | 1, 2, 3 → `READ-01`; 4, 5, 6 → `READ-02`; 7 → `APPLY-03`; 8, 9, 10 → `APPLY-01`; 11, 12 → `READ-03`; 13 → `OPEN-08`; 14 → `OPEN-14`; 15, 16 → `RUN-04`; 17, 18 → `READ-06`; 19 → `READ-06`, `RUN-07`; 20 → `APPLY-02`; 21, 22, 23 → `READ-08`; 24 → `OPEN-09` |
| Area D (write path) | D01–D04 → `APPLY-01`; D05 → `APPLY-02`; D06, D07 → `APPLY-01`; D08, D09 → `APPLY-03`; D10 → `OPEN-05`; D11 → `AGENT-04`; D12, D13 → `APPLY-06`; D14 → `APPLY-04`; D15, D16 → `APPLY-08`; D17 → `APPLY-07`; D18 → `APPLY-06`; D19 → `APPLY-05`, `APPLY-09`; D20, D21 → `APPLY-05`; D22 → `AGENT-03`; D23 → `APPLY-10`; D24 → `APPLY-11`; D25 → `APPLY-09` |

The architecture review's "journey B01", "A05" and "C07" are R3's ids: `APPLY-07`, `OPEN-05` and `RES-01`.

## 3. Existing tests at the wrong level

Some of today's tests check a business rule through the whole window, or pin the window's layout, so they
break when the window changes without having found a real problem. Others depend on the real parser and so
quietly skip on most machines. Each one below is moved, trimmed, deleted or kept, with the reason.

**Move** means the assertion is rewritten at the named level, then removed from the old test. **Delete**
happens only after the replacement is green. No row deletes a test that is the only check of something.

### 3.1 Whole-window walkthroughs

| Test | Decision | Reason | When |
|---|---|---|---|
| `AppSmokeTests.MainWindowCanBeConstructedHeadlessly` | Delete | `FirstProjectSmokeTests` constructs the window through the App's real composition, which proves strictly more | Phase 1 |
| `W1ChooseProjectAndCaptureBaselineTests.ChoosingProjectCapturingBaselineMakesAssessmentRunnable` | Move, then delete | The route becomes `FirstProjectSmokeTests`. The saved-copy, hash and inventory checks move to `FirstOpenRealClientTests` (I); the Selection summary to U | Phase 1 |
| `AssessmentWalkthroughTests.RunningAssessmentRendersReadingsAndPublishesStatistics` | Move, then delete | Exact readings, counts and statistics are parser and command facts. They move to `RunRealClientTests` with the fake parser (I) and to the real-parser lane (`RUN-11`) | Phase 2 |
| `ConformanceGrammarWalkthroughTests.RealWindowWalksConformanceGrammarFromBaselineToHandoff` | Move, then delete | 660 seconds of real parser through the window. The grammar facts move to `ConformanceGrammarAssessTests` and `RealParserLimitTests` (I, real parser) | Phase 3 |
| `ConformanceGrammarWalkthroughTests.DisposingWithAnAssessmentInFlightCancelsItWithoutBlockingTheDispatcher` | Move | A close rule, proven with the fake: `CloseCoordinatorTests` (U) | Phase 4 |
| `ConformanceGrammarWalkthroughTests.CopiedConformanceProjectLoadsWithThirteenLexicalEntries` | Keep | It guards the synthetic fixture | — |
| `CancelAssessmentWalkthroughTests.CancellingAssessmentLeavesNoInvocationAndAllowsARerun` | Move, then delete | The button goes to `CancelRunSmokeTests`; process and store invariants to `RunRealClientTests` with a held fake | Phase 1 |
| `CancelHandoffWalkthroughTests.CancellingHandoffDoesNotPublishPartialOutputAndRetrySucceeds` | Move, then delete | To `HandoffRealClientTests` with a held fake (I) | Phase 3 |
| `HandoffWalkthroughTests.WritingHandoffPublishesFilesForCompletedAssessment` | Move, then delete | File rules belong to `HandoffWriterTests`; the real-parser version to `RealParserHandoffTests` | Phase 3 |
| `UploadSimulationWalkthroughTests.ACompletedHandoffSurvivesFlatUploadAndStarterPromptPaste` | Move, then delete | To `HandoffRealClientTests` with the fake parser. `FakeChatReceiver` and its tests stay | Phase 3 |
| `SwitchProjectWalkthroughTests.ProjectMenuIsDisabledDuringARunAndSwitchingAfterCancelClearsTheFirstProject` | Move, then delete | The menu rule already has a U test; the rest goes to `ProjectIsolationRealClientTests` with a held fake | Phase 2 |
| `ConcurrentWalkthroughTests.TwoWindowsShareTheManagedRootWhileBothAssessmentsComplete` | Move, then delete | Two windows in one process prove neither windows nor processes. It becomes `ConcurrentRunTests` (I) | Phase 3 |
| `RestartAndSwitchWalkthroughTests.RestartingAndSwitchingProjectsKeepsOnlyTheSelectedProjectState` | Move, then delete | The route becomes `ReturnToProjectSmokeTests`; store tokens, Selection contents and hashes go to `ReopenRealClientTests` and `ProjectIsolationRealClientTests`. F08 first replaces its byte-written `.lock` | Phase 2 |
| `AvaloniaHeadlessPlatformTests`, `HoldingCommandClientTests`, `FakeChatReceiverTests` | Keep | They test the harness itself | — |

### 3.2 Windows over the fake client

These are U tests (§1.1). They keep binding and navigation checks and lose data checks. `MainWindowSmokeTests`
is renamed `MainWindowBindingTests` in the same lane, so the file stops claiming to be smoke.

| Test | Decision | Reason | When |
|---|---|---|---|
| `MainWindowSmokeTests.ComposeAttachesEveryPanelBoundToItsOwnChildViewModelAndSetsTheWindowsDataContext` | Keep | Binding | — |
| `.OverviewLoadsStoredNumbersThroughThePageContextAndItsTilesNavigateWithoutRunningAnAssessment` | Trim | Keep the tile clicks. The numbers, percentages and sentences go to `OverviewPageModelTests` (U) and `OverviewRealClientTests` (I) | Phase 5 |
| `.OverviewWithoutStoredAssessmentDoesNotShowZeroesAsResults`, `.OverviewSaysWhenTheDefaultSelectionHasNotResolvedAgainstTheBaseline` | Move | Page-model rules, with no window: `OverviewPageModelTests` | Phase 5 |
| `.EveryInputAndButtonHasAnAccessibleName` | Keep | A cheap metadata guard; its summary should say it reads Avalonia metadata, not what a screen reader receives | — |
| `.SwitchingTheThemeVariantWithARefusalAndAnInProgressStateBoundRaisesNoException` | Move | Becomes `ThemeSwitchTests`, which also checks that representative Intent colours change | Phase 5 |
| `.ClickingACompareCellListsOnlyItsWords` | Trim | The rule moves to `CompareViewModelTests.ClickingACellListsOnlyItsWords`; one click binding stays | Phase 5 |
| `.MatrixOffersOpinionsForAnAddedWordWithOneChosenAnalysis` | Trim | The one-reading rule is `CompareActionsTests`; keep the chooser binding | Phase 5 |
| `.FixFirstCollapsesToItsCountAndHighlightsTheFocusedWordWithItsReasonVisible` | Trim | Drop text-wrapping, class-name and theme-resource checks, which pin layout; keep collapse and focus | Phase 5 |
| `.AnalyzeTextsSwitchesBetweenTheReaderAndWordList` | Trim | Keep the switch; content goes to `ResultsInTextViewModelTests` and `TextsRealClientTests` | Phase 5 |
| `.ListsShowSeparateAiHandoffsForTheListAndItsTickedWords` | Keep | Buttons exist; `HandoffRealClientTests` now proves the request reaches the command | — |
| The other `MainWindowSmokeTests` cases (copyable text, links, file tiles, collection notice, starter prompt button, pasted words, measured width, narrowest window) | Keep | Presentation and binding | — |
| `WorkflowShellTests.ANarrowWindowCollapsesTheSidebarToIconsWithTooltipsAndKeepsTheBadges` | Trim | Drop the 204 and 52 pixel widths (`WorkflowShellTests.cs:219-260`); keep the collapse, tooltips and badges. Merge with `WorkspacePageTests.TheSidebarCollapsesToIconsBelowItsWidthThreshold` | Phase 5 |
| `WorkflowShellTests.TheShellTakesItsSizesAndMenuLookFromTheTokens` | Trim | Check that each size comes from a token key, not what the value is; values belong to the token tests | Phase 5 |
| `WorkflowShellTests.TheMatrixAndTheReviewPageShowTheSameChangesList` | Keep | Binding. It inserts a change by hand, so it proves nothing about the store; `CollectRealClientTests` does that | — |

### 3.3 Page and view models over the fake client

| Test | Decision | Reason | When |
|---|---|---|---|
| `ReviewPageModelTests` (all ten) | Keep | Enabled states and wording at U. Each data claim now has an I partner in `ReviewRealClientTests` | — |
| `PendingChangesViewModelTests`, `CompareActionsTests` | Keep | F16 adds one real-client replacement test and narrows the fake; do not convert these files wholesale | F16 |
| `TryWordPageTests` | Keep | Eligibility at U; `CollectRealClientTests` proves the stored identity | — |
| `WorkspacePageTests` freshness family | Keep | The right level for freshness rules. F04 may merge two of its sources of freshness | F04 |
| `StatisticsViewModelTests` | Deleted by F13 | The panel it tests is removed | F13 |
| `TextWordsViewModelTests` reader cases (`TextWordsViewModelTests.cs:285-365`) | Deleted by F12 | The reader is dead code | F12 |

### 3.4 Real-seam tests that move or change

| Test | Decision | Reason | When |
|---|---|---|---|
| `ReviewCommandClientTests.ASeededTrialFlowsThroughReviewApplyWithoutRewritingTheDraft` | Moved by F02 | Into a Commands test of `PendingChangesWorkflow` | F02 |
| `ReviewCommandClientTests.ApplyWaitsForTheProjectReadGate` | Replaced by F02 | It reaches a private semaphore by reflection | F02 |
| `PendingChangesTests`, `ProposalWorkflowTests` | Keep | Command-level I tests that happen to live in `Tests.Cli`. Moving them buys nothing | — |
| `AssessCommandTests.ProgressReportsOnlyTheCommandOwnedStagesWithNoPerWordTick` | Change if §8 question 7 is agreed | It pins stage-only progress | Phase 4 |
| `JobVerbArgvTests` | Keep | It fails when run alone without `MOTIF_DEVELOPER_COMMANDS=1`, because `test.ps1:69` sets it for the suite. It should set the variable itself, as `ProposalWorkflowTests` does | Phase 0 |
| `PageScreenshots`, `RealProjectScreenshots` | Keep, uncounted | Opt-in visual aids. `RealProjectScreenshots` reads real projects and must never become a fixture | — |

## 4. Test tools to build

The tests above need a few tools: a pretend parser that can be told to be slow, broken or stuck; a pretend
FieldWorks that can hold a project open or save a change to it; a way to close and reopen Motif inside a
test; a clock the test controls; time limits that say which step hung; and small made-up projects. F08
builds the foundation. This section adds only what F08 does not, and never a second copy of it.

**F08 builds, and this plan uses as is:** `MotifAppComposition.Create(MotifAppOptions)`, the one
composition root that `App` and `WalkthroughWindow` share; `IJobRunnerLauncher`, with
`ProcessRunnerLauncher` and `InProcessRunnerLauncher`; `TimeProvider` injected wherever the App reads the
clock; and `FieldWorksSimulator` with `Hold()`, `SaveEdit(Action<LcmCache>)` and `DeleteWordform(form)`.

### 4.1 The fake parser

`FakePanGloss` (`tests/FakePanGloss/Program.cs`) reads a behaviour file beside its own copy. It can already:
delay; hang while a heartbeat file exists; fail with an exit code and standard error; write no report;
stream batch progress; give per-word outcomes, including capped and timed out; answer `--describe` wrongly,
malformed, failing or hanging (sentinel files, `:69-92`); write a malformed grammar import (`:224`); return
a scripted trace from `parse` (`:273-318`), which is enough for Try a Word; run `grammar-health`; and crash
on `--crash-unhandled`.

Add, in `tests/FakePanGloss/Program.cs`:

| Behaviour | For | Why the existing ones do not do it |
|---|---|---|
| `malformedBatch`: write batch rows that do not parse | `RUN-08` | `malformedReport` exists only for `import` (`:224`) |
| `crashAfterWords: n`: an unhandled crash after n rows | `RUN-08` | `--crash-unhandled` is a flag the normal invocation never passes (`:58`) |
| `holdAfterWords: n`, and a `started` marker file | `RUN-02`, `RUN-09`, `RES-01`, `RES-02` | the heartbeat hang starts before the first word, so "cancel mid-run" is a race today |
| per-word `delayMs` | `RUN-09`, `RUN-10` | the delay is one number for the whole batch |

Add `FakeParserBehaviour`, a typed builder in `tests/SIL.Motif.Tests.Support/TestFixtures/` that writes the
behaviour file, so a test says `FakeParserBehaviour.HoldAfterWords(2)` rather than hand-written JSON.
Extend `FakeParser.cs` to copy the fake into a per-test folder, because the sentinels sit beside the
executable and would otherwise leak between tests.

### 4.2 The FieldWorks simulator

F08's `FieldWorksSimulator` holds the lock the way FieldWorks does, saves an edit made with LibLCM, and
deletes a wordform. Add, in the same file once F08 has landed:

| Addition | For |
|---|---|
| `Release()`: let go of the hold | `APPLY-07`, the FieldWorks smoke test |
| `SaveUnrelatedEdit()`: a save that touches no pending change | `APPLY-06` (a harmless save) |
| `DeleteAnalysis(id)`, `ChangeSpellingStatus(form)` | `APPLY-06` |
| `ReadBack(Func<LcmCache, T>)`: open a fresh copy after Motif has let go, and read it | `APPLY-05`, `APPLY-09` |

Retire the old ways of simulating FieldWorks: bytes written to a `.lock` file, which LibLCM does not see
(`RestartAndSwitchWalkthroughTests.cs:70`), and each test's own `FileStream` (`ProposalWorkflowTests.cs:475`).
F08 replaces the first; move the second to `Hold()` when the file is next touched.

### 4.3 Closing and reopening Motif

- **In-process restart**, for S and I: dispose the composed workspace and call `MotifAppComposition.Create`
  again over the same Motif root. Put it in `tests/SIL.Motif.Tests.Support/TestFixtures/MotifAppHost.cs`
  as `Restart()`. It is the whole seam for `ReturnToProjectSmokeTests` and `ReopenRealClientTests`.
- **Close while work runs**, for `RES-01`: a product seam, `CloseCoordinator`, that `MainWindow.Closing` and
  `App`'s `Exit` both await. The test drives it with a held fake. See §5.
- **A killed process**: kill the CLI or the runner child and its process tree, as `RunnerSpineTests` does.
  Killing the App process itself belongs to the native lane (§8 question 1); no headless test can.

**Decided with F08, 2026-09-26: the smoke tests share one real startup per process.** All five smoke tests
start Motif the way a person does, and they can now do so one after another inside a single test run.
Avalonia allows one setup and one application lifetime per process, and the lifetime's shutdown also ends
its dispatcher, so choice (a) of the F08 review holds, with these mechanics:

- `MotifAppHost` lives in `tests/SIL.Motif.Tests.App.Lifetime/MotifAppHost.cs`, not in Tests.Support, which
  every non-UI test project references and which carries no Avalonia. The smoke tests of §1.3 go in that
  assembly too, not under `tests/SIL.Motif.Tests.App/App/Smoke/`: the App test assembly already sets Avalonia
  up without a lifetime, and a process cannot set it up twice.
- The first `Start` calls `SetupWithLifetime`, which runs `App.OnFrameworkInitializationCompleted`. That
  override is one call to `App.StartDesktop(lifetime, options)`, and every later `Start` calls it directly
  into the same classic desktop lifetime, so each test gets a fresh window and workspace from the same code.
- `Stop` and `Restart()` close the session with `MotifDesktopSession.CloseAsync`, the method the lifetime's
  `Exit` calls, and wait for the Known-project load and the workspace's disposal before a test deletes its
  root. `Restart()` is the in-process restart this section asks for.
- The lifetime's own `Shutdown` runs once, from the collection fixture after the last test, and fails the run
  if that exit does not close the session. Pinned by `AppStartupCompositionTests` and `MotifAppHostExit`.

### 4.4 The clock

F08 injects `TimeProvider`. Tests use one fixed `TimeProvider` stub in
`tests/SIL.Motif.Tests.Support/TestFixtures/FixedClock.cs`, or `FakeTimeProvider` if the owner prefers the
package. F04 moves the shell's `When` formatting onto it. Also route `DateTimeOffset.Now` in
`AssessViewModel.cs:173` and `HandoffViewModel.cs:172` through it (F08's list). No test compares against
the wall clock.

### 4.5 Time limits that say which step hung

`WalkthroughWindow.WaitUntil` already takes a message (`WalkthroughWindow.cs:288-298`). Generalise it:

- `Step.Within(name, limit, action)` in `tests/SIL.Motif.Tests.Support/TestFixtures/Step.cs`. When the limit
  passes, it fails with the step's name, lists the child processes still alive (reuse
  `Walkthrough/PanglossProcesses.cs`), attaches the parser and runner logs, and kills those process trees so
  nothing outlives the test.
- Every S step and every I test that starts a child process uses it. The limits are those of §1.3: 30
  seconds a step, 60 a smoke test.
- `test.ps1` passes `--blame-hang-timeout` (10 minutes) to each project, so a hang anywhere names its test
  and fails, instead of running forever (deferred questions).
- A test that passes only on a retry is a failure: no retries are configured.

### 4.6 Fixtures

Every fixture is a blank LibLCM project built at run time, or the synthetic conformance project. None
contains real FieldWorks or Sena data.

| Fixture | What it is | Where | State |
|---|---|---|---|
| `SeededProject` | A scratch copy of a blank project with `SeededProject.Seed`'s tiny linguistic data | `PristineProjectFixture.cs`, `SeededProject.cs` | exists |
| `SeededEvidence` | `SeededProject` plus a stored run made by the real `AssessCommand` with the fake parser, so pages have evidence without the real parser | new `SeededEvidence.cs`, built on `SeededAssessment.cs` | new |
| `PendingChange` | `SeededEvidence` plus one change collected through the real pending-changes command; "measured" adds a finished check through `InProcessRunnerLauncher` | new `PendingChangeFixture.cs` | new |
| `TwoProjects` | Two `SeededEvidence` projects with different words and one change each | new `TwoProjectsFixture.cs` | new |
| `NoTextsProject` | A blank project with wordforms and no Texts | new `NoTextsProject.cs` | new |
| `LargeProject` | A generated project with a chosen number of Texts, lines and words, reproducible from a seed | new `LargeProjectGenerator.cs` | new |
| `OldSchemaStore`, `CorruptStore` | A project store stamped with another schema, or with its bytes overwritten | new `StoreFixtures.cs` | new |
| injected file system | A write that fails at a chosen stage, for `RES-07` | a seam in `PanGlossWorkspace` and the store's file writes | new, product seam |
| `Conformance` | The synthetic conformance project | `Conformance/`, `ConformanceProject.cs` | exists |
| empty root | A fresh Motif root and private writing-system store per test | `ProcessWritingSystemRepository.cs` | exists |

Two fixture chores from the deferred questions: `PristineProjectFixture` should sweep its own stale folders
at start (a killed test leaves 90 MB behind), and `trace-details-v2-matinlu.json` should have its origin
recorded before any new test builds on it (§6, the claims list).

## 5. Buttons, commands and states Motif is missing

Some workflows cannot be tested because Motif does not do them yet. These are product work, and each is
listed with the workflow that needs it and the words a person would see. Window words follow ADR 0046: no
Proposal, Draft, Preflight or `motif …` on screen. Review changes also avoids "Assessment", "Assessor" and
"regression" (F03). CLI words stay the CLI's.

| Capability | Needed by | What the person sees | Notes |
|---|---|---|---|
| The Known-project list reloads after a capture, and when the project menu opens | `OPEN-01`, `OPEN-05` | The project under **Open recent** at once; a project whose file has gone drops off | Bug 2. Shell work, so it goes to F05's implementer or waits for batch 3 |
| **Back** and **Forward** | `OPEN-10` | Two top-bar buttons, "Back" and "Forward", with the tooltip "Back to Texts"; Alt+Left and Alt+Right | P2. A page the App opens by itself after a run is not a visit |
| A keyboard route through Review changes and Apply | `OPEN-12` | Tab reaches every change, **Check these changes** and **Apply to FieldWorks project**, with visible focus; Enter activates | No new words |
| A message when two runs share no words | `RUN-04` | "These two runs have no words in common, so there is nothing to compare." What changed stays visible | Bug 6 |
| Guidance when PanGloss is missing | `RUN-07` | "Motif could not find PanGloss, so it cannot measure words. Install PanGloss beside Motif, then try again." Details: the places Motif looked | A window sentence in F06's table; no file picker |
| Per-word progress for a run | `RUN-09` | "12 of 40 words · kalamu", as Review's check already shows | §8 question 7. The Contract `AssessmentProgress` gains a finished count, a total and the current word |
| `assess` stops cleanly on Ctrl+C | `AGENT-02` | CLI: "Cancelled; nothing was stored." and the existing cancelled exit code | Bug 8 |
| Released verbs for an agent's changes | `AGENT-03` | CLI: `put-pending-change`, `remove-pending-change`, `recheck-pending-changes`, `preflight`, `trial --pending`, `apply --all-pending` without `MOTIF_DEVELOPER_COMMANDS` | §8 question 3 |
| `apply --all-pending [--revision <r>]` and `trial --pending --words … --wait` | `AGENT-03` | CLI verbs | F02 builds them |
| `grammar check --project <fwdata> [--json]` | `AGENT-01`, `READ-07` | CLI verb | F07 builds it |
| A warning before Apply when an approved analysis would be lost | `APPLY-08` | "Applying these changes would lose an approved analysis for 2 words: ikala, mutu. Change or remove the changes that cause it before applying." Apply stays off with that reason | Uses F03's typed numbers; the CLI keeps `--force` |
| **Refresh the numbers** on the Receipt | `APPLY-09` | A button on the "Applied to the FieldWorks project" card that runs the ordinary Refresh | Never automatic (ADR 0046 decision 1) |
| The last Receipt after reopening | `APPLY-09` | The "Applied to the FieldWorks project" card, with its date, until the next change | §8 question 5. A store read, so it may stay window-only (ADR 0043 decision 3) |
| A close that waits for work to stop | `RES-01` | "Stopping the run before closing…" for as long as it takes | Bug 7. A `CloseCoordinator` that `Closing` and `Exit` await |
| A way to make a file write fail on purpose | `RES-07` | Nothing; a test seam | In `PanGlossWorkspace` and the store's file writes |
| Handing off chosen words | `READ-08` | The existing AI Handoff page, now working | Bug 5 |
| Undo an Apply | `APPLY-11` | Not proposed | §8 question 8 |
| Recreate an unusable store from the window | `RES-03` | Not proposed; see §8 question 9 for the recommended sentence | Rule 18 |

## 6. Bugs the planners found

While planning, the seven reports and the architecture review found things that are wrong today. Each was
checked against the code for this plan. "Confirmed" means the code was read and does what the bug says;
none was run. "Unconfirmed" means the reading supports a risk but not a failure. The fixes go first where
they are cheap and touch nobody else's files (§7, phase 0).

| # | Bug | Evidence | Status | Workflow |
|---|---|---|---|---|
| 1 | The App does not turn off Windows crash dialogs; the CLI and the runner do | `src/SIL.Motif.App/Program.cs:8-14`; `src/SIL.Motif.Cli/Program.cs:29`; `src/SIL.Motif.Worker/Program.cs:46`. `CrashDialogsTests` launches its child from a test process that already suppresses the dialog, so it cannot catch this | Confirmed | `RES-05` |
| 2 | Open recent misses a project opened this session, and keeps one whose file has gone, until Motif restarts | The list loads only at startup (`src/SIL.Motif.App/App.axaml.cs:24,47-56`); nothing reloads it after a capture (`HandoffWorkspaceViewModel.cs:331-339`) | Confirmed | `OPEN-01`, `OPEN-05` |
| 3 | After Apply in one project, the next project says "Numbers need refresh"; a Review error from one project shows in the next | `AppliedSinceRefresh` is cleared only by `PublishEvidence` (`WorkspaceContext.cs:213`), not by `ClearProject` (`:174-180`), and the top bar reads it (`HandoffWorkspaceViewModel.cs:163,179`). `ReviewPageModel.OnProjectCleared` keeps `ApplyError` and `MeasurementError` (`ReviewPageModel.cs:244-253`) | Confirmed; F05 fixes | `OPEN-06` |
| 4 | With FieldWorks holding the project and one change pending, activating the window faults and opening the project can crash the App | Pending-change reads open the live `.fwdata` (`PendingChanges.cs:38,46,81,300,349`), and `LcmFileLockedException` is not an `IOException`, so `ProjectStoreCommand` does not catch it. Traced by F01. R3 marked this journey "works" | Confirmed by reading, not run; F01 fixes | `APPLY-07` |
| 5 | Handing off chosen words to AI Handoff is always refused | `HandoffViewModel.cs:163-164` sends `Assess: true` without a run id; `HandoffCommand.cs:87-90` refuses that before its own fresh-run branch (`:241-269`) | Confirmed | `READ-08` |
| 6 | What changed disappears when two runs share no words, hiding its own explanation | `HasDifference` is `Moves.Count > 0` (`DifferenceViewModel.cs:93`) and hides the panel root (`DifferencePanel.axaml:9`), which holds `OnlyInOneText` (`:18`) | Confirmed | `RUN-04` |
| 7 | Closing the window does not wait for running work to stop | `Exit` starts `DisposeAsync` and forgets it (`App.axaml.cs:23`); `Closing` only saves the bounds (`MainWindow.axaml.cs:35`) | Confirmed; what it leaves behind is unconfirmed | `RES-01` |
| 8 | `motif assess` ignores an interrupt | No cancellation token reaches `AssessCommand.Assess` (`src/SIL.Motif.Cli/Program.cs:778-780`), and nothing handles Ctrl+C | Confirmed; whether the parser outlives it is unconfirmed | `AGENT-02` |
| 9 | Apply stays enabled when the checked numbers show a lost approved analysis; the window hears only when Apply is refused | `CanApply` has no such term (`ReviewPageModel.cs:91-94`) | Confirmed (a missing capability) | `APPLY-08` |
| 10 | Apply cannot be cancelled and can hold the project gate for up to two minutes | `ReviewPageModel.cs:177` passes `CancellationToken.None`; the waits in `JobCommands.cs` use `Thread.Sleep` | Confirmed; F02 fixes | `APPLY-05` |
| 11 | An exception while opening a project ends the process | `OnProjectChosen` is `async void` (`HandoffWorkspaceViewModel.cs:328-329`) | Confirmed (by the code's shape); F05 fixes | `RES-05` |
| 12 | The Receipt is gone after reopening the project | It lives only in the page model and is cleared with the project (`ReviewPageModel.cs:67-70,244-253`) | Confirmed (a missing capability) | `APPLY-09` |
| 13 | A run reports stages only, though the parser boundary reads each finished word | `AssessmentProgress.cs:3-5` says PanGloss has no such signal; `PanGlossInvoker.cs:290` reads it for the Review check | Confirmed; the contract comment is out of date | `RUN-09` |
| 14 | `JobVerbArgvTests` fails when run on its own | It runs `new`, `dry-run` and `trial` without `MOTIF_DEVELOPER_COMMANDS`; only `test.ps1:69` sets it | Confirmed (a test bug) | `AGENT-06` |
| 15 | The reader may say "choose a Text" in a project that has none | Its messages branch at `ResultsInTextViewModel.cs:120-140`; no fixture has a Baseline with no Texts | Unconfirmed | `OPEN-08` |
| 16 | A long Text may be slow to show | The reader is an `ItemsControl` in a `ScrollViewer` (`ResultsInTextPanel.axaml:50-58,107`) with no virtualisation | Unconfirmed (a risk) | `OPEN-14` |

**Claims in the reports that the code does not support:**

- "`trace-details-v2-matinlu.json` is missing and breaks `PageScreenshots`." It is in
  `tests/SIL.Motif.Tests.Support/TestFixtures/` and copied to `bin/Debug/tests/TestFixtures/` by
  `SIL.Motif.Tests.Support.csproj:23`. Its origin is not recorded; check that it holds no real project data
  before a new test builds on it.
- "The window's jobs view shows an interrupted job" (R3). The App has no job view; only the pending-changes
  adapter reads job status. Jobs are watched through the CLI (`AGENT-06`).
- "Crash dialogs are covered for the child boundary" (R1). They are, for the CLI and the runner. The App is
  bug 1.
- "FieldWorks holding the project: works" (R3). See bug 4.

## 7. Build order

The work runs in phases that fit around the architecture batches now being built, so that nobody edits a
file someone else is changing. Cheap fixes and test tools start now; the three core smoke tests follow as
soon as the composition root exists; the rest follows the finding it depends on.

### 7.1 The architecture batches, and which files they hold

| Batch | Findings | Files that are busy while it runs |
|---|---|---|
| 1 (running) | F01, F02, F12 | `PendingChanges.cs`, `ProjectStoreCommand.cs`, `MotifSchema.cs`; the command client unit; the Catalog unit (`CommandCatalog.cs`, `CliVerbCatalog.cs`, `Cli/Program.cs`, `CommandTextRenderer.cs`); `ReviewPageModel.cs`; `ReviewCommandClientTests.cs`; `TextWordsViewModel.cs`; three panel code-behinds |
| 2 | F05, F07, F13, F14 | The shell unit (`WorkspaceContext.cs`, `HandoffWorkspaceViewModel.cs`, `PageModel.cs`); `SetupViewModel.cs`, `ChangesViewModel.cs`, `ReviewPageModel.cs`; the Catalog unit; `TimingPageModel.cs` and the statistics panel; `TextWordsQuery.cs`, `BaselineCaptureCommand.cs`, `MotifSchema.cs` |
| 3 | F03, F08, F09 | `ReviewNumbersCommand.cs`, `ReviewPageModel.cs`, the CLI renderer; `App.axaml.cs`, the new composition file, `RunnerKick.cs`, `RunnerOptions.cs`, `WalkthroughWindow.cs` and the walkthrough tests, `FieldWorksSimulator.cs`; F09's view models, `AssessCommand.cs`, `TimingWordSet.cs` |
| 4 | F04 | The shell unit and every page model; `AssessViewModel.cs`, `StoredAssessmentView.cs`, `CurrentEvidenceQuery.cs`, `AssessCommand.cs` |
| 5 | F10, F11 | The real command adapter, `CommandRunViewModel.cs`, `TextWordsViewModel.cs`; the page models of Texts, Try a Word, Timing and Setup; the shell unit |
| 6 | F06 | `UserFacingRefusal.cs` and about twelve view models and five views |
| 7 | F16, F17 | `FakeCommandClient*.cs`, `HoldingCommandClient*.cs` and the App tests that use them; the views' code-behind, `App.axaml.cs`, `WalkthroughWindow.cs` |
| 8 | F15 | Every file that names the shell view model, tests included |

### 7.2 Rules for every lane

- New tests go in new files, named for their subject (§1.3 and §2 name them). Edit an existing test file
  only in the lane that §3 assigns to it.
- A test that opens an `LcmCache`, including every S test and every real-client test, joins
  `LcmCacheTestCollection`.
- Real-client and S tests build the window and its page models through `MotifAppHost` (§4.3), never by
  calling page-model constructors, so F11's constructor changes do not break them.
- No workflow id, finding id or phase number in any test name, comment or string.
- Build with `./build.ps1` and prove with `./test.ps1 -Configuration Release`, as AGENTS.md says.
- Other branches were checked on 2026-09-25. `perf/waits-seam`, `perf/pristine-seed` and
  `perf/runner-spine-flake` are about 249 commits behind `main` and edit the old single `tests/SIL.Motif.Tests`
  project, which no longer exists; `perf/build-test-architecture`, `fu/no-crash-dialogs` and
  `feat/review-tests` are merged. None of them lands first, and phase 0 does not wait for them. If
  `perf/runner-spine-flake` is revived, its `RunnerSpineTests` fix lands before lane 2D relies on that test.

### 7.3 Phases

**Phase 0, now, beside batches 1–3.** Test tools, fixtures and fixes that no batch touches. Three lanes.

| Lane | Work | Product files | Test files |
|---|---|---|---|
| 0A tools | The fake parser's new behaviours and their builder (§4.1); `Step.Within` (§4.5); the fixed clock stub; `--blame-hang-timeout` | `tests/FakePanGloss/Program.cs`, `test.ps1` | `tests/SIL.Motif.Tests.Support/TestFixtures/FakeParserBehaviour.cs`, `FakeParser.cs`, `Step.cs`, `FixedClock.cs`; new `tests/SIL.Motif.Tests.LibLcm/Parser/FakePanGlossBehaviourTests.cs` |
| 0B fixtures | `SeededEvidence`, `PendingChange` (unmeasured), `TwoProjects`, `NoTextsProject`, `LargeProject`, the two store fixtures; `PristineProjectFixture` sweeps stale folders; `JobVerbArgvTests` sets its own variable | none | `tests/SIL.Motif.Tests.Support/TestFixtures/SeededEvidence.cs`, `PendingChangeFixture.cs`, `TwoProjectsFixture.cs`, `NoTextsProject.cs`, `LargeProjectGenerator.cs`, `StoreFixtures.cs`, `PristineProjectFixture.cs`; `tests/SIL.Motif.Tests.Cli/Cli/JobVerbArgvTests.cs`; new `tests/SIL.Motif.Tests.Commands/Commands/TextInventoryEmptyTests.cs`, `LargeProjectTests.cs` |
| 0C free fixes | Bugs 1, 5 and 6, each test first; the successful CLI `assess` round trip (no product change) | `src/SIL.Motif.App/Program.cs`; `src/SIL.Motif.Commands/Handoff/HandoffCommand.cs`; `src/SIL.Motif.App/ViewModels/DifferenceViewModel.cs`, `Views/DifferencePanel.axaml` | new `tests/SIL.Motif.Tests.App/App/EntryPointStartupTests.cs`, `DifferencePanelTests.cs`; new `tests/SIL.Motif.Tests.Commands/Handoff/HandoffSelectedWordsTests.cs`; new `tests/SIL.Motif.Tests.Cli/Cli/AssessRoundTripArgvTests.cs` (without the interrupt case) |

Bug 2, the Known-project reload, is shell work. Hand it to F05's implementer in batch 2, with
`KnownProjectsRefreshTests.cs`; if F05 is already under way, do it during batch 3, when the shell is free.

**Phase 1, the minimal P0 smoke suite, as soon as batch 3 (F08) merges.** It runs beside batch 4. Two
lanes, because the smoke tests delete walkthroughs whose lower-level checks must first move.

| Lane | Work | Test files |
|---|---|---|
| 1A smoke | `MotifAppHost`; the first three smoke tests; then delete `AppSmokeTests`, `W1ChooseProjectAndCaptureBaselineTests` and `CancelAssessmentWalkthroughTests` once lane 1B is green | new `tests/SIL.Motif.Tests.Support/TestFixtures/MotifAppHost.cs`; new `tests/SIL.Motif.Tests.App/App/Smoke/FirstProjectSmokeTests.cs`, `CancelRunSmokeTests.cs`, `ChangeToReceiptSmokeTests.cs`; the three deleted files |
| 1B moved checks | The I checks those deletions need | new `tests/SIL.Motif.Tests.App/App/RealClient/FirstOpenRealClientTests.cs`, `RunRealClientTests.cs` |

**Phase 2, the P0 checks below the window, after batch 4 (F04).** Four lanes on disjoint files. The
last two smoke tests join lane 2A. One ordering dependency: `SameProjectTwoProcessTests` in lane 2C uses the
measured `PendingChange` that lane 2B adds, so write it after 2B's fixture change merges.

| Lane | Work | Product files | Test files |
|---|---|---|---|
| 2A return and FieldWorks | `OPEN-05`–`OPEN-07`, `RUN-03`, `RUN-07`, `RES-03`; the simulator additions (§4.2); the last two smoke tests; delete `RestartAndSwitchWalkthroughTests`, `SwitchProjectWalkthroughTests`, `AssessmentWalkthroughTests` | none | new `App/RealClient/ReopenRealClientTests.cs`, `ProjectIsolationRealClientTests.cs`, `OverviewRealClientTests.cs`, `ActivationRealClientTests.cs`, `ParserAbsentRealClientTests.cs`, `StoreRefusalRealClientTests.cs`; new `App/Smoke/ReturnToProjectSmokeTests.cs`, `FieldWorksBesideMotifSmokeTests.cs`; `tests/SIL.Motif.Tests.Support/TestFixtures/FieldWorksSimulator.cs` |
| 2B write path | `APPLY-01`–`APPLY-07` below the window; the measured `PendingChange` | none | new `App/RealClient/CollectRealClientTests.cs`, `ReviewRealClientTests.cs`, `ApplyReadBackTests.cs`; `PendingChangeFixture.cs` |
| 2C agent | `AGENT-01`–`AGENT-05`, `RES-04`; bug 8 (the Catalog unit is free from batch 4) | `src/SIL.Motif.Cli/Program.cs` | new `tests/SIL.Motif.Tests.Cli/Cli/AgentParityArgvTests.cs`, `AgentChangesArgvTests.cs`, `AgentHandoffArgvTests.cs`, `ApplyForceArgvTests.cs`; `AssessRoundTripArgvTests.cs` (the interrupt case); new `tests/SIL.Motif.Tests.App/App/RealClient/PendingChangesConcurrencyTests.cs`, `SameProjectTwoProcessTests.cs` |
| 2D parser and runs | `RUN-02`, `RUN-08`, `RUN-10`, `RES-02` | none | new `tests/SIL.Motif.Tests.Commands/PanGloss/PanGlossBatchFailureTests.cs`; `tests/SIL.Motif.Tests.Commands/Commands/AssessParserFailureTests.cs`, `ConcurrentRunTests.cs`; `tests/SIL.Motif.Tests.Cli/Integration/TrialKillRecoveryTests.cs`, `AgentJobsArgvTests.cs`; delete `ConcurrentWalkthroughTests` |

**Phase 3, P1 below the window, beside batches 5–7.** Test files only, plus the product capabilities that
wait for `ReviewPageModel.cs` to be free (after batch 6).

| Lane | Work | Product files | Test files |
|---|---|---|---|
| 3A read the results | `READ-02`–`READ-07`, `RUN-04`–`RUN-06`, `OPEN-08`, `OPEN-09` below the window | none | new `App/RealClient/TextsRealClientTests.cs`, `TimingRealClientTests.cs`, `TryWordRealClientTests.cs`, `WarningsRealClientTests.cs`, `SetupRealClientTests.cs` (after F11); new `App/EmptyStateTests.cs`, `PageStateRetentionTests.cs`, `SetupLimitsTests.cs`, `ExpectedAnalysisTests.cs` |
| 3B AI Handoff and the real parser | `READ-08`, `RUN-11`; delete `HandoffWalkthroughTests`, `CancelHandoffWalkthroughTests`, `UploadSimulationWalkthroughTests`, and the long conformance walkthrough | none | new `App/RealClient/HandoffRealClientTests.cs`; new `tests/SIL.Motif.Tests.Commands/Commands/ConformanceGrammarAssessTests.cs`, `RealParserLimitTests.cs`, `RealParserHandoffTests.cs` |
| 3C Review changes, during batch 7 | The loss warning, the Receipt's Refresh step, the Receipt after reopening, one Apply at a time (`APPLY-05`, `APPLY-08`–`APPLY-10`), in the page model only | `src/SIL.Motif.App/ViewModels/ReviewPageModel.cs`; a stored-Receipt read in Commands. `Views/ReviewPanel.axaml` waits until batch 7 merges, because F17 changes every panel's bindings | new `App/ReviewLossWarningTests.cs`, `ReceiptNextStepTests.cs`, `ApplyOutcomeTextTests.cs`, `KeepEditingTests.cs` |
| 3D progress, during batch 7 | Per-word run progress (`RUN-09`), if §8 question 7 is agreed | `src/SIL.Motif.Contract/Responses/AssessmentProgress.cs`, `src/SIL.Motif.Commands/Assess/AssessCommand.cs`, `CommandRunViewModel.cs` | new `tests/SIL.Motif.Tests.Commands/Commands/AssessProgressTests.cs`, `App/RunProgressTextTests.cs`; `AssessCommandTests.cs` (the stage-only test) |

**Phase 4, after batch 8 (F15).** The shell and window work that F17 and F15 would otherwise collide with.
Both lanes edit `Views/MainWindow.axaml.cs`, so they run one after the other: 4A first, then 4B. The
`ReviewPanel.axaml` part of lane 3C (the warning, the Refresh button, the restored Receipt card) runs
beside 4A, since neither touches the other's files.

| Lane | Work | Product files | Test files |
|---|---|---|---|
| 4A closing | `RES-01`: `CloseCoordinator`, awaited by `Closing` and `Exit`; move the in-flight dispose test | `App.axaml.cs`, `Views/MainWindow.axaml.cs`, a new `Services/CloseCoordinator.cs` | new `App/CloseCoordinatorTests.cs`, `App/RealClient/CloseRealClientTests.cs`; `ConformanceGrammarWalkthroughTests.cs` (remove the moved case) |
| 4B navigation | `OPEN-10` Back and Forward; `OPEN-12` keyboard route; `AGENT-03` activation binding | the shell view model, `Views/MainWindow.axaml(.cs)` | new `App/NavigationHistoryTests.cs`, `NavigationHistoryViewTests.cs`, `KeyboardRouteTests.cs`, `MainWindowActivationTests.cs`, `ProjectOpenFailureTests.cs` |

**Phase 5, after phase 4.** Clean-up of the shared test files, one lane, so nothing else edits them at the
same time: every "trim" and "move" row of §3.2, the rename of `MainWindowSmokeTests` to
`MainWindowBindingTests`, `ThemeSwitchTests`, `OverviewPageModelTests`, and the P2 checks (`OPEN-14`'s
reader test, `RES-07`'s file-system seam in `PanGlossWorkspace`).

### 7.4 What "done" means for a phase

The phase's new tests pass in `./test.ps1 -Configuration Release`; every walkthrough it deletes has its
checks green at the new level first; the S suite stays within §1.3's budget, measured, not estimated; and the
real-parser lane's skip count is printed.

## 8. Questions for the owner

A few choices belong to the owner. Each has a recommended answer, and the plan follows that answer until the
owner says otherwise. Rulings already recorded (bulk Candidate versus individual opinions, `--force` never
reaching a change that no longer fits, the per-word time limit staying, no grammar check on opening, and
the shared-words rule for losses) are not asked again.

1. **Should there be a native Windows test lane, driving the built App through UI Automation?** Only it can
   prove what a screen reader hears, keyboard focus, OS dialogs, and killing the App process.
   *Recommended:* yes, later and small: one FlaUI test that launches the built App, finds the project menu,
   Review changes and Apply by name, and moves focus with the keyboard. Run it on a Windows runner with a
   desktop session, outside `./test.ps1`, and do not count it as S.
2. **Should a skipped real-parser test be louder?** Today they skip silently in every worktree, and eleven
   skip because PanGloss v0.3.3 has no `assess`. *Recommended:* a separate CI step runs the real-parser lane
   with PanGloss installed and fails if any of its tests skipped; `./test.ps1` prints the skip count.
3. **Should an agent's change verbs be released?** `put-pending-change`, `remove-pending-change`,
   `recheck-pending-changes`, `preflight`, `trial` and `apply` are developer-only today, so an agent on a
   released build cannot collect or apply a change, and a separate FieldWorks could not run
   `apply --all-pending`. *Recommended:* release the pending-change verbs, `preflight`, `trial --pending` and
   `apply --all-pending` in 0.2, which the roadmap gives to "push analyses"; keep authoring verbs such as
   `add-set-gloss` developer-only.
4. **When the window is activated after another Motif process wrote to the store, what reloads?** Today only
   the pending changes. *Recommended:* reload every stored page (Overview, Texts, Timing, Warnings, the
   pending changes), never start a run, and keep Refresh as the only thing that captures and measures.
5. **What should reopening a project restore?** *Recommended:* the last Receipt, yes, because it is the only
   confirmation of what was written and it is already stored; the last page, no: every project opens on
   Overview.
6. **Do Reject and Back to candidate follow Approve's "one word, one chosen analysis" rule?** The deferred
   questions mark this for confirmation. *Recommended:* yes; the code enforces it today and tests pin it.
7. **Should a run show how many words are done, and which word is being parsed?** *Recommended:* yes. The
   parser boundary already reads it, and Review's check already shows it. Carry it through the Contract's
   `AssessmentProgress`, and test it below the window.
8. **Should Apply be undoable?** *Recommended:* not before 1.0. An undo needs its own semantics (a reverse
   Proposal, checked like any other), and the Receipt already records what changed. Say so in the Receipt's
   help text instead.
9. **Should the window offer to recreate a store from another Motif version?** Rule 18 says such a store is
   refused and the developer is told to delete it. *Recommended:* no button. The window names the file:
   "Motif's file for this project, *path*, was made by a different version of Motif. Close Motif, delete
   that file, and open the project again. Changes not applied yet are lost; your FieldWorks project is not
   touched."
10. **Should closing the window during a run cancel it, or let it finish in the background?**
    *Recommended:* cancel it cleanly and keep nothing partial (`RES-01`). A run that survives closing needs a
    durable Assessment job first, and that is a product decision, not a test.

## 9. Owner rulings, 2026-09-25 (grilling)

The owner answered §8 and the follow-up questions the same day. Where a ruling differs from a recommendation above, the ruling wins. The tests of §2 and the build order of §7 follow it.

1. **Native Windows lane (question 1): yes, small.** The five smoke tests start through the real startup: the same `App`, `OnFrameworkInitializationCompleted` and classic desktop lifetime, with Avalonia.Headless and Skia (`SetupWithLifetime`). Only the parser, the pickers and the clock are swapped. A handful of FlaUI tests drive the built exe on this machine, outside `./test.ps1` and hosted CI. They cover native dialogs, the UIA tree, activation and DPI, which headless can't prove.
2. **Real-parser skips (question 2): yes.** Run a separate real-parser lane, and make any skip in it loud.
3. **Agent change verbs (question 3): not now.** The pending-change verbs, `preflight`, `trial --pending` and `apply --all-pending` stay developer-only. The full set of AI commands comes in a later release, after its own design and test coverage. `AGENT-03` is tested with developer commands on.
4. **Reloading on window activation (question 4): yes, stored data only.** Refresh is always manual. It records when it ran and when the FieldWorks project was last saved (CONTEXT.md, Refresh).
5. **Reopening a project (question 5):** it opens on Overview. The History page (item 13 below) takes the place of "the last Receipt".
6. **One word, one analysis (question 6):** unchanged from the recorded rulings.
7. **Per-word progress (question 7): no.** Words will be parsed in parallel, so a run shows no "current word". `RUN-09` asserts only stage progress, and the missing-capability row for per-word progress is withdrawn.
8. **Undo an Apply (question 8): no.** A change can be undone only while it is still in Review changes. The Receipt says there is no undo.
9. **A store Motif refuses (question 9): before 1.0, a button deletes it.** The window names the file and offers a button that deletes it so Motif can recreate it. After 1.0, stored shapes need migrations. 1.0 is the release with real Proposals (AGENTS.md rule 18).
10. **Closing during a run (question 10), replaced.** A popup names the running work. Enter picks the default; Esc means Keep Motif open. The words:

    > **Motif is still measuring words**
    > Measuring 40 words in *Texts*, started 2 minutes ago.
    > [Keep Motif open] [Stop measuring and close] [**Close and finish in the background**]

    Work that can't be stopped (Apply, Baseline capture) gets no choice: "Motif is applying your changes to the FieldWorks project. It will close as soon as that's done."

    To support this, the window's Assessments, and the measuring step of Refresh, move to the background job runner. Cancel becomes a quick stop request. Reopening a project shows a run that finished while Motif was closed, or its live progress. `motif assess` as a background job for agents waits for the AI-commands release. `RES-01` changes to test all three choices.
11. **Crash popup (bug 1): yes.** `Program.Main` calls `CrashDialogs.Suppress()`. Errors that escape the UI thread open a Motif error window with:
    - a plain summary;
    - expandable details;
    - **Copy details**, **Save report**, **Email maintainer** and **Close**.

    Email opens the mail program with a short body, and the full report is a saved `.txt` to attach. Closing that window closes Motif. The address is `john_lambert@sil.org`, held in one `SupportEmail` constant next to Motif's version. The model is libpalaso's WinForms `ExceptionReportingDialog`; no Avalonia version exists yet.
12. **Texts and changes: keep the Baseline plus dots.** A preview "as it would be after these changes" (a Dry Run on a copy) waits for 1.0. Add a scale test with a Proposal of thousands of changes. F14's stored text words go ahead. They are display data only, never fit evidence.
13. **History, P1, after this test work:**
    - **(a) A message at Apply.** The person can give a message at Apply, pre-filled with a summary ("approved 10 words, added analysis for 52 words"). It is limited to the existing 128-character description, with a live counter, and is checked as they type.
    - **(b) A "since Motif's last change" line** on Overview and the Receipt. It uses only cheap, true facts:
      - the last Apply, who made it, and its message;
      - whether FieldWorks saved since;
      - Send/Receive authors, read with FLEx Bridge's `hg.exe` when `.hg` exists;
      - entries and texts changed, by `DateModified`.

      It never counts word-analysis changes or claims who made a local edit.
    - **(c) An exact "something changed" check.** Each Receipt stores the project's save time and semantic fingerprint after Apply.
