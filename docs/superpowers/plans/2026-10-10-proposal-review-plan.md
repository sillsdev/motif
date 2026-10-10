# Proposal review implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**In plain terms:** this plan builds the Proposals page, Proposal mode and the Track-Changes style review in five lanes.
One lane goes first and gives the evidence a name; four lanes then run side by side on the window's pages, the
Proposals page, the review list and approving a parse inside a Proposal; a closing lane regenerates media and Help
and runs the full validation. Each lane is one squashed commit on `main`, gated by the whole suite.

**Goal:** a person reviews the Proposals that AI assistants and they themselves put together, inside the existing
window, through one set of pages that show "today → with this Proposal".

**Architecture:** `EvidenceSet` names the evidence a page reads; `Trials` reads a Proposal's latest Trial as a record;
the window publishes an `EvidenceLens` of one or two sets through the hook every page already has; `ProposalReview`
yields one ordered list of word and grammar changes with Keep and Undo; `proposals` is a Released scorecard both front
ends read.

**Tech Stack:** .NET 10, Avalonia with Semi.Avalonia and CommunityToolkit.Mvvm, SQLite through `MotifDatabase`,
xUnit, the fake PanGloss, walkthrough scripts.

**Spec:** `docs/superpowers/specs/2026-10-10-proposal-review-design.md` (sections cited as §n). ADR draft:
`docs/adr/0061-proposal-mode-is-a-lens-over-one-set-of-pages.md`. Architecture scan:
`_briefs/proposal-review/architecture-review.html`.

## Global constraints

- Build and test only with `./build.ps1` and `./test.ps1`; the merge gate for every lane is
  `pwsh ./test.ps1 -All -Configuration Release` with `MOTIF_PANGLOSS_EXE` set to the pinned parser and
  `MOTIF_SCREENSHOTS` set (AGENTS.md, "DONE for UI").
- Comments: one line, at most 110 characters for implementation comments; no plan, issue, date or status references;
  API docs complete (AGENTS.md, Comments). Tokens: no literal colour or size in a view or component; new colours as
  Primitives, new meanings as Intent keys listed in the Intent header (AGENTS.md, Design tokens).
- Every Released command has a `commands.json` entry (Title ≤ 30 characters, Description 1 to 3 sentences) and a
  `commands/<slug>.md` page; `HelpCatalogTests.EnglishCatalogCoversEveryReleasedCommandAndRequiredTerms` enforces it.
- Every catalogued command has exactly one CLI verb (`CommandCatalogParityTests`); every `ICommandClient` member maps
  to a catalog name or is listed in `ReadsThatNeverWriteTheStore` (`ICommandClientCatalogParityTests`); every new
  refusal code is added to `ExpectedRefusalCodes`.
- Rules 4, 5, 6, 15, 17, 18 of AGENTS.md: one Proposal is one LibLCM unit of work; order is declared, never
  positional; Rebase alters nothing of intent; the Runner stays storage-agnostic; every ADR and plan section opens in
  plain words; no migration code, a refused store is deleted and recreated.
- No author, actor or user is recorded anywhere (spec D15); the window never shows the words Draft, revision, intent
  digest, Trial, Preflight or EvidenceSet (§2).
- Nothing re-runs on its own (ADR 0035, ADR 0046 decision 1).
- Each lane lands on `main` as one squashed commit with a plain user-facing message, after a green gate.

## Review focus

Inputs the spec implies but no task's tests exercise by default; each line names the owning task where its test is added.

1. **A Trial whose scratch was cleaned up.** Try a Word in Proposal mode and the fit check of a staged parse must refuse
   with "try again", never read the Baseline as if it were the scratch. Test added to Lane D task D3.
2. **Refresh while in Proposal mode.** The bar must say "Needs another try", the Apply card must block, the Proposals
   page must regroup, and nothing must re-run. Test added to Lane A task A2 and Lane B task B3.
3. **Undo of the last entry.** A Proposal with zero operations: the review list is empty, Apply is blocked with
   "Nothing to apply", and the Proposal is not deleted. Test added to Lane C task C4.
4. **Two Proposals that change the same word.** Both cards carry `AlsoChangedBy`; opening either works; Apply of one
   makes the other's Trial `BaselineMoved` after Refresh. Test added to Lane B task B1.
5. **A Proposal with no Trial opened in Proposal mode.** Every paired value has `HasAfter == false`, the bar says
   "Not tried yet", and the pages render today's numbers without a refusal. Test added to Lane A task A1
   (`EvidenceLensContractTests`).

---

## Lane order and ownership

| Order | Lane | Worktree branch | Owns | Must land before |
|---|---|---|---|---|
| 1 | **L0 evidence-set** | `feat/pr-evidence-set` | `SIL.Motif.Commands/Queries/EvidenceSet.cs`, `Commands/Trials.cs`, `Queries/CurrentEvidenceQuery.cs`, `Catalog/OverviewCommand.cs`, `Queries/StoredGrammarCheckQuery.cs`, `Queries/InspectQuery.cs`, `Queries/WordTraceQuery.cs`, `Selection/SelectionReader.cs` (evidence binding only), `JobCommands.cs`, `PendingChangesWorkflow.cs` (Measure), `ProposalCommands.SoundSystem.cs:372`, `Worker.Runtime/Jobs/TrialJobHandler.cs`, `Worker.Runtime/Store/AssessmentRepository.cs`, `Host/Store/MotifSchema.cs`, `Contract/Responses/Paired.cs`, `Contract/Requests/*` (`ProposalId` members), `App/ViewModels/EvidenceLens.cs`, `App/ViewModels/PageModel.cs`, `App/ViewModels/WorkspaceContext.cs` (lens state), `CONTEXT.md`, `docs/adr/0061*`, `docs/proposal-lifecycle.md` | A, B, C, D |
| 2 | **LA lens-pages** | `feat/pr-lens-pages` | `App/Views/MainWindow.axaml(.cs)` (bar), `App/Tokens/Primitives.axaml`, `Intent.axaml`, `Tokens/Components/ProposalBar.axaml`, `Tokens/Components/Text.axaml` (paired classes), `App/ViewModels/{OverviewPageModel, TimingPageModel, WarningsPageModel, TextsPageModel, CompareViewModel, DifferenceViewModel, StatisticsViewModel, WorkspaceShellViewModel}.cs`, `Views/Pages/{OverviewPage, TimingPage, WarningsPage, TextsPage}.axaml`, `Views/ComparePanel.axaml`, `Views/DifferencePanel.axaml`, `Views/MatrixCell*`, `Views/Pages/PageRegistry.cs` (guide code), `HelpPopupViewModel.cs`, `guide/proposal-mode.md`, `tests/.../EvidenceLensContractTests.cs`, `PageScreenshots.cs`, `StateScreenshots.cs` | E |
| 2 | **LB proposals-page** | `feat/pr-proposals-page` | `Commands/Catalog/ProposalStandingsCommand.cs`, `Contract/Responses/ProposalStandings.cs`, `Cli/CliVerbCatalog.cs` (`proposals`), `Mcp/AgentTools.cs` (`motif_proposals`), `App/ViewModels/ProposalsPageModel.cs`, `Views/Pages/ProposalsPage.axaml(.cs)`, `ViewModels/WorkspacePage.cs` (`Proposals`), `AutomationIds.cs` (page and rows), `Tokens/Components/Proposals.axaml`, `App.axaml` (include), `Help` (`proposals` command page, `guide/proposals.md`, `terms`), `walkthroughs/proposals-open.*`, `WalkthroughFixtureSeeder` (`proposal-ready`) | E |
| 2 | **LC review-list** | `feat/pr-review-list` | `Commands/Review/ProposalReview.cs`, `Commands/Review/ReviewMarks.cs`, `Contract/Responses/ProposalReviewResponse.cs`, `Contract/Requests/ReviewMarkRequest.cs`, `PendingChangesWorkflow.cs` (Apply generalized), `ProposalCommands.cs` (Apply `User` removal), `Cli/CliVerbCatalog.cs` (`proposal review`, `proposal looked-at`, `apply --proposal`), `App/ViewModels/{ReviewPageModel, ReviewListEntryViewModel, ReviewChangeGroupViewModel, RetirementProposalReviewViewModel}.cs`, `Views/ReviewPanel.axaml(.cs)`, `Views/RetirementProposalReviewPanel.axaml`, `KeyboardShortcutRegistry.cs` (Review scope), `Tokens/Components/Review.axaml`, Help (`proposal review`, `apply --proposal`, `guide/review-a-proposal.md`, `review-changes.md`), `walkthroughs/proposal-review-keep-undo.*` | E |
| 2 | **LD parse-in-proposal** | `feat/pr-parse-in-proposal` | `Commands/PendingChanges.cs`, `Commands/ChangeFitPreflight.cs`, `Contract/Requests/PendingChangesRequests.cs` (`ProposalId`), `App/ViewModels/{ChangesViewModel, ResultsTokenViewModel, ResultsInTextViewModel*, AnalysisMarkingState, WordDisposition}.cs`, `Controls/WordPresentation/WordCard*`, `Views/WordDispositionButtons.axaml`, `Tokens/Components/WordCard.axaml`, Help (`guide/change-an-analysis.md` addition), `walkthroughs/proposal-approve-parse.*` | E |
| 3 | **LE media-and-validation** | `feat/pr-media` | `media.json`, `site/fixtures/walkthroughs/*`, `tests/.../WalkthroughBaselines/*`, `tests/SIL.Motif.Tests.Cli/Golden/help-all-current.json`, `docs/current-architecture.md`, `README.md` (one paragraph) | — |

Shared files touched by two lanes are listed with the owning lane; the other lane adds only through the owner's
produced interface. `CliVerbCatalog.cs`, `CommandCatalog.cs`, `App.axaml`, `commands.json`, `terms.json` and
`ExpectedRefusalCodes` are append-only for L B, C and D; conflicts there are integrator rebases, not lane work.

---

## Lane L0: evidence-set (first)

**Scope.** §3, §4, §14, §15 and the window-side lens core of §5.1. Produces every name the other lanes consume. Lands
the ADR as accepted and the CONTEXT.md terms.

**Tests.** Unit and Integration in `SIL.Motif.Tests.Commands`, `SIL.Motif.Tests.Worker`, `SIL.Motif.Tests.Contract`,
`SIL.Motif.Tests.App`. No walkthrough or screenshot change.

**Exit criteria.** Gate green; every existing page compiles against `OnEvidencePublishedAsync(EvidenceLens, …)`
with no behaviour change; `motif overview --proposal <id> --json` prints the Trial's numbers for a seeded Proposal;
`Decisions` is gone from `MotifSchema` and an old store is refused.

### Task L0-1: `EvidenceSet` and `EvidenceSets.Current`

**Files:**
- Create: `src/SIL.Motif.Commands/Queries/EvidenceSet.cs`, `src/SIL.Motif.Commands/Queries/EvidenceSets.cs`
- Modify: `src/SIL.Motif.Commands/Queries/CurrentEvidenceQuery.cs` (selection logic moves; `CurrentEvidenceSnapshot.Set` added)
- Test: `tests/SIL.Motif.Tests.Commands/Commands/EvidenceSetsTests.cs`

**Interfaces:**
- Produces: `EvidenceOriginKind { Project, Trial }`; `EvidenceOrigin(Kind, ProposalId?, IntentDigest?, JobId?)`;
  `EvidenceSet(BaselineToken Baseline, string? RootAssessmentId, IReadOnlyList<string> ReplacementAssessmentIds, IReadOnlyDictionary<string,string> MeasurementIds, string? GrammarCheckId, IReadOnlyList<string> MeasuredWords, EvidenceOrigin Origin)`;
  `EvidenceSets.Current(MotifDatabase, ProjectLocator) : CommandOutcome<EvidenceSet?>`; `CurrentEvidenceSnapshot.Set : EvidenceSet?`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void CurrentNamesTheLatestParseTimeRunAndItsSameInvocationMeasurements()
  // seed: Baseline, Default Selection, ParseTime + Correctness + ObjectTiming of one invocation, one rerun replacing two words
  // assert: set.RootAssessmentId == parseTime.Id; set.ReplacementAssessmentIds == [rerun.Id];
  //         set.MeasurementIds[AssessmentKinds.Correctness] == correctness.Id; set.Origin.Kind == Project
[Fact] public void CurrentIsNullBeforeAnyAssessment()           // Baseline only → Value is null, Succeeded
[Fact] public void CurrentNeverSelectsATrialAssessment()        // a newer Assessment with ProposalId set is ignored
[Fact] public void SnapshotCarriesTheSameSetCurrentReturns()    // CurrentEvidenceQuery.ReadCurrentEvidence(...).Value.Set equals EvidenceSets.Current(...)
```

- [ ] **Step 2: Run to verify they fail** — `./test.ps1` with `--filter EvidenceSetsTests`; expected: compile failure on the missing types.
- [ ] **Step 3: Implement** the records and `EvidenceSets.Current`, moving lines 112 to 170 of `CurrentEvidenceQuery.ReadCurrentEvidence` into it and calling it from there.
- [ ] **Step 4: Run to verify they pass.**
- [ ] **Step 5: Commit** — "Name the evidence every page reads".

### Task L0-2: `Trials` and `TrialRecord`

**Files:**
- Create: `src/SIL.Motif.Commands/Trials.cs`
- Modify: `src/SIL.Motif.Worker.Runtime/Jobs/TrialJobHandler.cs` (`TrialCompletion` gains `AllWords`), `src/SIL.Motif.Worker.Runtime/Store/AssessmentRepository.cs` (`GetHeader` variants without the `ProposalId IS NULL` filter; `ListByProposalAndDigest`)
- Test: `tests/SIL.Motif.Tests.Commands/Commands/TrialsTests.cs`

**Interfaces:**
- Consumes: L0-1 `EvidenceSet`.
- Produces: `TrialFreshness { Current, BaselineMoved, ProposalChanged, Incomplete, None }`;
  `TrialRecord(string JobId, string ProposalId, string IntentDigest, BaselineToken Baseline, DryRunProjection? DryRun, IReadOnlyDictionary<string,string> AssessmentIds, IReadOnlyList<string> Words, bool AllWords, TrialLimits? Limits, JobStatus Status, DateTimeOffset? CompletedUtc, TrialFreshness Freshness)`;
  `Trials.LatestFor(MotifDatabase, ProjectLocator, string proposalId) : TrialRecord?`; `Trials.ListFor(...) : IReadOnlyList<TrialRecord>`;
  `EvidenceSets.ForTrial(MotifDatabase, ProjectLocator, TrialRecord) : CommandOutcome<EvidenceSet?>`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void LatestForReadsTheNewestCompletedTrialOfTheProposal()   // two trial jobs; the newer's job id, its Dry Run from DryRunJson, AssessmentIds by kind from ResultJson
[Fact] public void FreshnessIsBaselineMovedWhenTheCurrentBaselineTokenDiffers()
[Fact] public void FreshnessIsProposalChangedWhenTheDraftDigestDiffers()  // edit the Draft JSON after the Trial
[Fact] public void FreshnessIsIncompleteWhileTheJobRuns()
[Fact] public void LatestForIsNullWithoutATrial()
[Fact] public void ForTrialBuildsASetWithTrialOriginAndTheSourceBaselineToken()
```

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** `Trials` over `JobRepository.ListByProjectAndKind(projectKey, "trial")` and `AssessmentRepository.ListByProposal`, parsing `TrialJobInput` and `TrialCompletion`; compute `Freshness` against `BaselineRepository.GetCurrentEvidence` and `IntentDigest.Compute(BuildProposalJson(draft) or revision)`.
- [ ] **Step 4: Run to verify they pass.**
- [ ] **Step 5: Commit** — "A Proposal's latest Trial is read as one record, with its freshness".

### Task L0-3: every evidence read takes a set

**Files:**
- Modify: `src/SIL.Motif.Contract/Requests/{OverviewRequest, GrammarCheckRequest, InspectRequest}.cs` and `src/SIL.Motif.Commands/Queries/WorkbenchQueries.cs` (`WordTraceRequest`) (`string? ProposalId = null`), `src/SIL.Motif.Commands/Catalog/OverviewCommand.cs`, `Queries/StoredGrammarCheckQuery.cs`, `Queries/InspectQuery.cs`, `Queries/WordTraceQuery.cs`, `Queries/WarningWordsQuery.cs`, `Queries/StoredParseWarnings.cs`, `Selection/SelectionReader.cs` (delete `SelectionAssessmentEvidence`; `OpenSelectionReaderRequest.ShownAssessment : EvidenceSet?`), `src/SIL.Motif.Cli/*` (the `--proposal` option on `overview`, `warnings`, `timing`, `stats`, `inspect`, `trace`)
- Test: `tests/SIL.Motif.Tests.Commands/Commands/EvidenceSetReadsTests.cs`, existing `OverviewCommandTests`, `SelectionReaderTests`

**Interfaces:**
- Produces: `SelectionReader` validates a set whose `Origin.Kind == Trial` by Proposal id, intent digest, source token and the exact companion ids it names (`ExpectedContextRepository.MatchesTrialRoot`), never by `MatchesNamedRoot` or the `ProposalId IS NULL` measurement lookups; `OverviewCommand.Overview(OverviewRequest)` resolving `ProposalId` through `Trials.LatestFor` + `EvidenceSets.ForTrial`; `OverviewCommand.Overview(MotifDatabase, ProjectLocator, EvidenceSet)` overload; the same pair for `StoredGrammarCheckQuery.Query`; `InspectQuery` and `WordTraceQuery` reading `set.Baseline`, with the Trial case reading the Trial job's scratch directory when it exists and otherwise refusing `trial.scratch-gone` ("The Proposal's copy is gone. Try it again."); new refusal codes `trial.none`, `trial.scratch-gone` added to `ExpectedRefusalCodes`.

- [ ] **Step 1: Write the failing tests** — `OverviewForATrialReadsTheTrialsAssessment` (seed a Trial with different outcomes; `TextCoverage` differs from the project's); `OverviewWithUnknownProposalRefusesTrialNone`; `StoredGrammarCheckForATrialReadsTheTrialsCheck`; `SelectionReaderOpensOverATrialSetWithTheSourceBaselineToken` (the root carries a `ProposalId`; its Correctness companion is resolved from the set, not from a Baseline lookup); `ATrialSessionReusesTheSourceBaselinesTextIndex` (no second `BaselineTextReadIndex` row is read or written); `ATrialRootIsNeverTheCurrentEvidence` (`ExpectedContextRepository.IsCurrentContext` stays false for it); `TraceForATrialWithoutScratchRefusesScratchGone`.
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement**, keeping each command's existing behaviour when `ProposalId` is null.
- [ ] **Step 4: Run to verify they pass**, and the whole Commands and Cli projects.
- [ ] **Step 5: Commit** — "Every read of stored evidence can be pointed at a Proposal's Trial".

### Task L0-4: the Trial checks the grammar and measures the Default Selection on demand

**Files:**
- Modify: `src/SIL.Motif.Worker.Runtime/Jobs/TrialJobHandler.cs`, `src/SIL.Motif.Host/Store/MotifSchema.cs` (`GrammarChecks.ProposalId NULL`), `src/SIL.Motif.Commands/Store/GrammarCheckRepository.cs` (`GetLatest(token, proposalId)`; project reads filter `IS NULL`), `src/SIL.Motif.Commands/JobCommands.cs` (`EnqueueDryRun` accepts a Draft), `src/SIL.Motif.Commands/PendingChangesWorkflow.cs` (`Measure` returns `MeasurePendingResult(TrialRecord Trial, ReviewNumbersResponse Numbers)`), `src/SIL.Motif.Contract/Responses/PendingChangesWorkflowResponses.cs`
- Test: `tests/SIL.Motif.Tests.Worker/Jobs/TrialJobHandlerTests.cs`, `tests/SIL.Motif.Tests.Commands/Commands/PendingChangesWorkflowTests.cs`, `JobCommandsTests`

- [ ] **Step 1: Write the failing tests** — `ATrialStoresAGrammarCheckOfTheScratchUnderItsProposalId`; `TheProjectsStoredGrammarCheckIgnoresTrialChecks`; `MeasurePendingReturnsTheTrialRecord` (`result.Trial.JobId` is the enqueued job; `Trial.Freshness == Current`); `EnqueueDryRunAcceptsADraftAndFreezesItsContent`; `TrialCompletionRecordsAllWords`; `ATrialRecordsTheScratchsProjectStandingForEachWord` (a Draft that approves an analysis: the Trial's `AssessedWord.ProjectStanding` for that word is Approved, so "with this Proposal" rows classify against the Proposal's own Opinions; research-4 asked whether staged Opinions reach the scratch, and this test answers it).
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement.** The grammar check runs `GrammarCheckQuery` against the scratch cache after the Dry Run step and before the parser step; a check failure is recorded on the job as a warning, not a Trial failure.
- [ ] **Step 4: Run to verify they pass.**
- [ ] **Step 5: Commit** — "A Trial also checks the grammar it tried, and a Draft can be dry-run".

### Task L0-5: composer provenance names its operations; no author anywhere

**Files:**
- Modify: `src/SIL.Motif.Commands/ProposalCommands.SoundSystem.cs:372` (every provenance entry carries `operationIds`), `src/SIL.Motif.Host/Store/MotifSchema.cs` (drop `Decisions`), `src/SIL.Motif.Contract/Requests/PendingChangesWorkflowRequests.cs` and `src/SIL.Motif.Commands/Requests/ProposalCommandRequests.cs` (delete `User`), `src/SIL.Motif.Commands/ProposalCommands.cs` (Apply without a user), `src/SIL.Motif.Cli/*` (the `--user` option goes), `src/SIL.Motif.App/ViewModels/ReviewPageModel.cs:524` (no `Environment.UserName`)
- Test: `tests/SIL.Motif.Tests.Commands/Commands/ComposerProvenanceTests.cs`, `tests/SIL.Motif.Tests.Commands/Store/MotifSchemaTests.cs` (new; the Commands test project already opens stores)

- [ ] **Step 1: Write the failing tests** — `EveryComposerProvenanceEntryListsTheOperationsItProduced` (author a sound rule through the composer; the entry's `operationIds` equal the added operation ids); `TheSchemaHasNoDecisionsTable`; `AStoreWithADecisionsTableIsRefusedNotMigrated`.
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run to verify they pass.**
- [ ] **Step 5: Commit** — "Each authored grammar change knows its operations, and nothing records a person".

### Task L0-6: the window's lens core

**Files:**
- Create: `src/SIL.Motif.App/ViewModels/EvidenceLens.cs`, `src/SIL.Motif.Contract/Responses/Paired.cs`
- Modify: `src/SIL.Motif.App/ViewModels/PageModel.cs` (`OnEvidencePublishedAsync(EvidenceLens lens, CancellationToken)`), `WorkspaceContext.cs` (`Lens`, `EvidenceLens`, `OpenProposalAsync`, `LeaveProposal`, `TryAgainAsync`; `RunPublicationAsync` publishes the lens; `ChangesViewModel.AssessmentId` follows the lens), `WorkspaceSelection.cs` (one reader per set), `ProjectEvidence.cs` (`Set : EvidenceSet?` from the snapshot), every `*PageModel.cs` override (`evidence` → `lens.Today`), `Services/ICommandClient.Workbench.cs` (`ReadTrialAsync(ReadTrialRequest) → TrialRecord?`, `EnqueueTrialAsync`, `OverviewAsync(OverviewRequest)` already takes the request)
- Test: `tests/SIL.Motif.Tests.App/App/EvidenceLensTests.cs`, `WorkspaceContextTests.cs` (lens state), `tests/SIL.Motif.Tests.Contract/Contract/PairedTests.cs`

**Interfaces:**
- Produces (App): `MarkupView { Today, TodayToProposal, Proposal }`; `ProposalLens(string ProposalId, string Label, string IntentDigest, TrialRecord? Trial, MarkupView View)`; `WorkspaceContext.Lens : ProposalLens?` (observable); `WorkspaceContext.EvidenceLens : EvidenceLens`; `Task WorkspaceContext.OpenProposalAsync(string proposalId, CancellationToken)`; `void LeaveProposal()`; `Task TryAgainAsync(CancellationToken)`; `EvidenceLens { ProjectEvidence Today; TrialEvidence? Proposal; MarkupView View; bool InProposalMode; IReadOnlyList<EvidenceSet> Sets; Paired<T> Pair<T>(Func<EvidenceSet,T>); Task<Paired<T>> PairAsync<T>(Func<EvidenceSet,CancellationToken,Task<CommandOutcome<T>>>, CancellationToken) }`; `TrialEvidence(EvidenceSet Set, TrialRecord Trial, IReadOnlyList<AssessmentWordResult> Words)`.
- Produces (Contract): `readonly record struct Paired<T>(T Before, T? After, bool HasAfter, bool Moved) { T Shown(MarkupView) }` serialised as `{ "before": …, "after": …, "hasAfter": …, "moved": … }`; `EvidenceLens.Pair`/`PairAsync` take an optional `Func<T,T,bool> moved` comparer. `WorkspaceSelection` becomes two owners, `Today` and `Proposal`, each with `Reader`, `Summary`, `Replacing` and leases; `WorkspaceContext.SelectionReads` keeps its name for `Today` and gains `ProposalSelectionReads`.
- Produces (ICommandClient): `Task<CommandOutcome<TrialRecord?>> ReadTrialAsync(ReadTrialRequest(ProjectPath, ProposalId), CancellationToken)` mapped to a new Developer Read command `proposal trial` with CLI verb `proposal trial`; `Task<CommandOutcome<JobEnqueuedResponse>> EnqueueTrialAsync(EnqueueTrialRequest, CancellationToken)` mapped to `trial`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void PairWithoutAProposalHasNoAfter()           // lens.Proposal null → Pair(read).HasAfter false, Before from Today
[Fact] public void PairReadsOncePerSet()                       // a counting read is called twice in Proposal mode
[Fact] public void MovedIsDecidedByTheComparerAndFalseWithoutOne()
[Fact] public async Task ProposalModeOpensASecondReaderAndLeaveDisposesOnlyIt()   // Today's reader and its leases survive
[Fact] public async Task OpenProposalPublishesALensWithTheTrialSet()   // FakeCommandClient.ReadTrialCompletesWith(record); pages receive lens.Proposal.Set.Origin.Kind == Trial
[Fact] public async Task LeaveProposalRepublishesTodayOnly()
[Fact] public async Task RefreshInProposalModeMarksTheTrialBaselineMovedAndRunsNothing()  // Review focus 2
[Fact] public void ShownFollowsTheMarkupView()                 // Today → Before; Proposal → After ?? Before
[Fact] public void PairedSerialisesAsBeforeAndAfter()          // Contract: canonical JSON has keys before, after
```

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement**; the mechanical hook change (`evidence` → `lens.Today`) is its own commit before any page pairs anything.
- [ ] **Step 4: Run to verify they pass**, plus the whole App test project.
- [ ] **Step 5: Commit** twice — "Pages receive the evidence as a lens, today's half filled" then "The window can open a Proposal and look through its Trial".

### Task L0-7: ADR accepted, glossary and lifecycle

**Files:**
- Modify: `docs/adr/0061-proposal-mode-is-a-lens-over-one-set-of-pages.md` (status accepted), `CONTEXT.md` (the four entries from the ADR, under "Evaluating a Proposal" after **Trial**), `docs/proposal-lifecycle.md` (the one sentence under "From Draft to Proposal"), `docs/adr/0046-pages-not-stages.md` (an Amendments section pointing at ADR 0061 for decisions 2 and 3)
- Test: none; `tools/verify-comment-only.ps1` does not apply.

- [ ] **Step 1: Edit the four documents.**
- [ ] **Step 2: Commit** — "Proposal mode, evidence sets, looked at, Keep and Undo join the glossary".

---

## Lane LA: lens-pages

**Scope.** §5.3, §5.4 (Overview, Texts Matrix and What changed, Timing, Warnings, badges), §5.5, §11, the Help guide
`proposal-mode`, Proposal-mode page and state screenshots. The word card block is Lane D's.

**Tests.** Unit (`SIL.Motif.Tests.App`): `EvidenceLensContractTests`, per-page paired tests, `ComponentStyleTests`
cases, `DesignTokenTests`; screenshots (`PageScreenshots`, `StateScreenshots`) gated by `MOTIF_SCREENSHOTS`.

**Exit criteria.** Gate green with captures; `EvidenceLensContractTests` passes for every registry entry; the bar,
paired tiles and moved cells appear in the Proposal-mode captures in light and dark at 1040 and 1240.

### Task A1: the seam test and the paired text classes

**Files:**
- Create: `tests/SIL.Motif.Tests.App/App/EvidenceLensContractTests.cs`
- Modify: `src/SIL.Motif.App/Tokens/Components/Text.axaml` (`TextBlock.pairedBefore`, `.pairedArrow`, `.pairedAfter`, `.pairedAfter.up`, `.pairedAfter.down` using `Intent.Proposal.Accent`), `tests/.../ComponentStyleTests.cs` (cases), `tests/.../FakeCommandClient.Workbench.cs` (`ReadTrialCompletesWith`, `OverviewForProposalCompletesWith`, …)
- Create: `src/SIL.Motif.App/Views/PairedText.cs` (a `TemplatedControl` with `Value : IPaired`, `Format`, `View` properties rendering "7 → 8" or "7", and `Classes="moved"` when `Value.Moved`)
- Modify: `src/SIL.Motif.App/ViewModels/PageModel.cs` (`PairedOutputs : IReadOnlyList<PairedOutput>`; `protected void RegisterPaired(string name, Func<IPaired> read)`), `src/SIL.Motif.Contract/Responses/Paired.cs` (`IPaired { bool HasAfter; bool Moved; object? BeforeValue; object? AfterValue }`)

- [ ] **Step 1: Write the failing tests** — `EveryPageSurvivesALensWithBothSets`, `EveryRegisteredOutputHasADistinctAfterInProposalMode` (sentinel values differ per set), `EveryRenderedPairedTextShowsBothSentinels`, `EveryRegisteredOutputMatchesTodayWithoutAProposal`, `APageOverAProposalWithNoTrialShowsTodaysNumbers` (Review focus 5), `PairedTextShowsAnArrowOnlyWithAnAfterAndMovedOnlyWhenTold`.
- [ ] **Step 2: Run to verify they fail** (no page has a `Paired<T>` property yet; the test discovers them by reflection and asserts at least the shape rules hold).
- [ ] **Step 3: Implement** the control and the test harness.
- [ ] **Step 4: Run to verify they pass.**
- [ ] **Step 5: Commit** — "Every page is checked once for showing today beside a Proposal".

### Task A2: tokens and the context bar

**Files:**
- Modify: `src/SIL.Motif.App/Tokens/Primitives.axaml` (`Primitive.Color.Purple25/200/300/500/600/800/975`, with `Purple500` `#9E28B3`), `Intent.axaml` (the `Intent.Proposal.*` keys in both themes and the header), `App.axaml` (include `ProposalBar.axaml`), `Views/MainWindow.axaml` (new root row `ProposalBar` between `TopBar` and the notices grid; `Classes.proposal="{Binding Context.EvidenceLens.InProposalMode}"` on the Window; the same class on `FlyoutPresenterClasses` of the Help and Settings flyouts and on the word card popup root), `ViewModels/WorkspaceShellViewModel.cs` (`ProposalBar` view model: `Label`, `TriedText`, `FreshnessSentence`, `View`, `TryAgainCommand`, `LeaveCommand`), `AutomationIds.cs` (`ProposalBar`, `ProposalBarLeave`, `ProposalBarTryAgain`, `ProposalBarView`)
- Create: `src/SIL.Motif.App/Tokens/Components/ProposalBar.axaml`
- Test: `tests/.../WorkspaceShellViewModelTests.cs` (bar sentences), `ComponentStyleTests` (bar cases), `ComponentStateContractCases` (hover and selected on the three-way switch), `DesignTokenTests` (header lists the new keys)

- [ ] **Step 1: Write the failing tests** — `TheBarIsHiddenOutsideProposalMode`; `TheBarNamesTheProposalAndWhenItWasTried`; `TheBarSaysNeedsAnotherTryAfterRefresh` (Review focus 2); `EscapeWithNothingFocusedLeaves`; `TheWindowRootCarriesTheProposalClassOnlyInProposalMode`; `AFlyoutInProposalModeResolvesProposalStyles`; `TodayViewInProposalModeKeepsTheBarAndTheLens` (the view is a projection, not Leave); token tests.
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement.** Freshness sentences exactly as §5.3.
- [ ] **Step 4: Run to verify they pass.**
- [ ] **Step 5: Commit** — "A purple bar names the open Proposal and offers Try again and Leave".

### Task A3: Overview pairs its tiles

**Files:**
- Modify: `src/SIL.Motif.App/ViewModels/OverviewPageModel.cs` (`Overview : Paired<OverviewResponse?>`; every text property becomes `Paired<string>`; `LookFirstRows` becomes `MovesRows` in Proposal mode built from `DifferenceViewModel.Moves`), `Views/Pages/OverviewPage.axaml` (`PairedText` in the tiles; two bar rows in Speed and Text coverage), `ViewModels/DifferenceViewModel.cs` (`LoadSources(today, proposal, "Today", "With this Proposal")` entry)
- Test: `tests/.../OverviewPageModelTests.cs` (new file)

- [ ] **Step 1: Write the failing tests** — `TilesReadBeforeAndAfterInProposalMode` ("7 → 8 of 9 words parse"); `LookFirstBecomesWhatThisProposalMoves` (rows from moved words, each opening the Matrix cell); `WithoutAProposalTheOverviewIsUnchanged` (golden strings from today's tests).
- [ ] **Step 2 to 5** as above. Commit — "The Overview shows today → with this Proposal".

### Task A4: Texts pairs the Matrix and What changed

**Files:**
- Modify: `src/SIL.Motif.App/ViewModels/TextsPageModel.cs`, `CompareViewModel.cs` (`CompareViewModel ProposalCompare` loaded from `lens.Proposal.Words`; a cell's `MovedCount`; `MovedFilter`), `AssessViewModel.cs` (`Difference` fed from the lens in Proposal mode), `Views/ComparePanel.axaml`, `Views/MatrixCell.axaml(.cs)` (`Classes="moved"` outline in `Intent.Proposal.Accent`; header "Same 1 → 2"), `Views/TextWordsPanel.axaml` (the "With this Proposal" column), `ViewModels/TextWordsViewModel.cs` (the column's value from the Trial reader session)
- Test: `tests/.../CompareViewModelTests.cs`, `TextsPageModelTests.cs`

- [ ] **Step 1: Write the failing tests** — `AMovedCellCarriesBothCounts`; `MovedByThisProposalListsOnlyWordsWhoseCellChanged`; `WhatChangedComparesTodayWithTheProposalInProposalMode`; `OutsideProposalModeTheMatrixIsUnchanged`; `EnteringProposalModeKeepsTheWordListRowsAndSelection` (same row view model instances, same `SelectedItem`, paired fields updated in place); `AWordTheTrialDidNotMeasureReadsNotMeasuredWithThisProposal`.
- [ ] **Step 2 to 5.** Commit — "The Matrix shows which cells a Proposal moves".

### Task A5: Timing and Warnings pair

**Files:**
- Modify: `src/SIL.Motif.App/ViewModels/TimingPageModel.cs` (paired stats, kind shares and rule rows; "new" and "removed" marks from `lens.Proposal.Trial.DryRun.Effects`), `StatisticsViewModel.cs`, `Views/Pages/TimingPage.axaml`, `Views/TimingKindBar.cs` (two rows on one scale), `ViewModels/WarningsPageModel.cs` and `GrammarWarningsViewModel.cs` (paired counts; chips "All 24 → 22", "Gone with this Proposal", "New with this Proposal"; a gone row struck through with "Fixed by: <entry title>" when the Dry Run touched the warned object), `Views/Pages/WarningsPage.axaml`
- Test: `tests/.../TimingPageModelTests.cs`, `WarningsPageModelTests.cs`

- [ ] **Step 1: Write the failing tests** — `RuleTimeReadsBeforeAndAfter`; `ARuleTheProposalCreatesIsMarkedNew`; `WarningCountsPair`; `AGoneWarningNamesTheChangeThatFixedIt`; both pages unchanged without a Proposal.
- [ ] **Step 2 to 5.** Commit — "Timing and Warnings show what a Proposal changes".

### Task A6: delta badges, registry guide codes, Help and captures

**Files:**
- Modify: `src/SIL.Motif.App/ViewModels/WorkspacePage.cs` (`PageViewModel.DeltaBadge`), `PageModel.cs` (`DeltaBadge`), `Views/MainWindow.axaml` (badge template), `Tokens/Components/Sidebar.axaml` (`.proposalDelta`), `Views/Pages/PageRegistry.cs` (`PageEntry.GuideCode`), `ViewModels/HelpPopupViewModel.cs` (`GuideCode` reads the registry; the switch goes), `tests/.../PageScreenshots.cs` (`OpenOverSampleDataInProposalMode`; `p-1-overview`, `p-2-texts-matrix`, `p-4-timing`, `p-5-warnings`), `tests/.../StateScreenshots.cs` (`proposal-bar` ×3 views)
- Create: `src/SIL.Motif.Help/Content/en/guide/proposal-mode.md`
- Test: `HelpPopupViewModelTests` (registry-driven theory), `WorkflowShellTests`

- [ ] **Step 1: Write the failing tests** — `EveryRegistryEntryHasAGuidePage`; `TheSidebarShowsADeltaBadgeInProposalMode`.
- [ ] **Step 2 to 5.** Commit — "The sidebar shows what each page gains with the Proposal, and Help knows every page".

---

## Lane LB: proposals-page

**Scope.** §6, the `proposals` command, the Proposals page, the `proposal-ready` fixture and the `proposals-open`
walkthrough, Help for `proposals` and `trial`.

**Tests.** Integration (`SIL.Motif.Tests.Commands`): `ProposalStandingsCommandTests`; Unit (`SIL.Motif.Tests.App`):
`ProposalsPageModelTests`; System: `ProposalsOpenWalkthroughReplayTests`; `HelpCatalogTests`; `CommandCatalogParityTests`;
MCP golden `tools/list`.

**Exit criteria.** Gate green; `motif proposals --json` lists the pending Draft and a seeded Proposal with paired
numbers; the walkthrough baselines exist and `media.json` lists them; the sidebar test lists nine pages.

### Task B1: the `proposals` command

**Files:**
- Create: `src/SIL.Motif.Commands/Catalog/ProposalStandingsCommand.cs`, `src/SIL.Motif.Contract/Responses/ProposalStandings.cs`, `src/SIL.Motif.Contract/Requests/ProposalStandingsRequest.cs`, `src/SIL.Motif.Help/Content/en/commands/proposals.md`, `commands/trial.md`, `commands/trial-wait.md`
- Modify: `Catalog/CommandCatalog.cs` (`proposals` Released Read, agent tool `motif_proposals`; `trial`, `trial --wait` to Released), `Cli/CliVerbCatalog.cs`, `Mcp/AgentTools.cs` (`motif_proposals` calls `proposals`), `commands.json`, `terms.json` (`trial`)
- Test: `tests/SIL.Motif.Tests.Commands/Commands/ProposalStandingsCommandTests.cs`

**Interfaces:**
- Consumes: L0 `Trials`, `EvidenceSets`, `Paired<T>`, `ReviewNumbersCommand`, `OverviewMetrics`, `TimingAggregation`.
- Produces: `ProposalGroup { WaitingForYou, NeedsAnotherTry, NotAppliedYet, SetAside }`; `ProposalStandings(IReadOnlyList<ProposalCard> Proposals, int SetAsideCount)`; `ProposalCard(ProposalId, Label, Status, Group, IsPendingDraft, IReadOnlyList<ChangeKindCount> ChangeKinds, TrialSummary? Trial, IReadOnlyList<string> AlsoChangedBy)`; `ChangeKindCount(string Kind, int Count)`; `TrialSummary(JobId, CompletedUtc, Freshness, AllWords, Paired<double?> TextCoveragePercent, Paired<int> ApprovedKept, Paired<int> ApprovedTotal, Paired<double?> WordTimeMs, Paired<int?> WarningCount, int LostApproved, int NoLongerFits)`; `ICommandClient.ProposalStandingsAsync(ProposalStandingsRequest, CancellationToken)`.

- [ ] **Step 1: Write the failing tests** — `ListsThePendingDraftUnderNotAppliedYet`; `AProposalWithACurrentTrialWaitsForYou`; `AProposalWhoseBaselineMovedNeedsAnotherTry`; `ANeverTriedProposalNeedsAnotherTry`; `NumbersPairTheProjectsAndTheTrials` (coverage before and after from seeded outcomes); `TwoProposalsChangingOneWordNameEachOther` (Review focus 4); `SetAsideIsCountedNotListedByDefault`; `ChangeKindsCountRulesAllomorphsAndApprovedParses`.
- [ ] **Step 2 to 5.** Commit — "motif proposals lists every Proposal with what it does to the numbers".

### Task B2: the Proposals page

**Files:**
- Create: `src/SIL.Motif.App/ViewModels/ProposalsPageModel.cs`, `Views/Pages/ProposalsPage.axaml(.cs)`, `Tokens/Components/Proposals.axaml`, `src/SIL.Motif.Help/Content/en/guide/proposals.md`
- Modify: `ViewModels/WorkspacePage.cs` (`Proposals` after `Overview`), `Views/Pages/PageRegistry.cs` (second entry, guide code `proposals`), `AutomationIds.cs` (`ForPage(Proposals)` → `motif-page-proposals`; `ForProposalRow(id)`, `ProposalsOpen`, `ProposalsTryAgain`), `App.axaml`, `tests/.../WorkspacePageTests.cs` (nine pages), `ComponentStyleTests` cases, `HelpPopupViewModelTests` row, `PageScreenshots.Views()` (`"9-proposals"`)
- Test: `tests/SIL.Motif.Tests.App/App/ProposalsPageModelTests.cs`

**Interfaces:**
- Consumes: B1 `ProposalStandings`; L0 `WorkspaceContext.OpenProposalAsync`, `TryAgainAsync`.
- Produces: `ProposalsPageModel { IReadOnlyList<ProposalGroupViewModel> Groups; IAsyncRelayCommand<ProposalCardViewModel> OpenCommand; IAsyncRelayCommand<ProposalCardViewModel> TryAgainCommand; IRelayCommand ShowSetAsideCommand; string SetAsideLinkText }`; `ProposalCardViewModel` with `KindsText` ("2 rules · 1 allomorph · 1 parse approved"), `Coverage`, `ApprovedKept`, `WordTime`, `Warnings` as `Paired<string>`, `Chips`.

- [ ] **Step 1: Write the failing tests** — `GroupsFollowTheResponse`; `OpenEntersProposalMode` (context `Lens.ProposalId` set, `CurrentPage == Overview`); `TryAgainEnqueuesATrialAndRefreshesTheList`; `TheBadgeCountsWaitingForYou`; `KindsTextReadsInWords`; `TheSidebarListsNinePages`.
- [ ] **Step 2 to 5.** Commit — "A Proposals page lists what is waiting, what needs another try and your own changes".

### Task B3: the `proposal-ready` fixture and the `proposals-open` walkthrough

**Files:**
- Create: `walkthroughs/proposals-open.walkthrough.json`, `src/SIL.Motif.Help/Content/en/walkthroughs/proposals-open.json`, baselines under `tests/SIL.Motif.Tests.App/Assets/WalkthroughBaselines/proposals-open/`
- Modify: `tests/.../Walkthrough/WalkthroughReplayTests.cs` (`ProposalsOpenWalkthroughReplayTests`), `WalkthroughFixtureSeeder.Seeders` (`proposal-ready`: `RealCommandClient` authors one sound rule into a Draft `review-sample` through the sound-rule composer, finalizes it, runs `trial --wait` with `AllWords` on the fake parser whose `_fake-pangloss.json` moves one word from No parse to Same, and stages one approve into `pending-changes`), `media.json`
- Test: the walkthrough itself; `WalkthroughScriptLoaderTests` wrapper rule

- [ ] **Step 1: Write the script** with steps `open-proposals` (click `motif-page-proposals`), `proposals` (capture, callouts on the three group headers), `open-first` (click `motif-proposals-open`), `overview-with-proposal` (waitFor `motif-proposal-bar` visible; capture with callouts on the bar and one paired tile).
- [ ] **Step 2: Run it with `MOTIF_WALKTHROUGH_UPDATE_BASELINES=1`**, inspect the PNGs, commit the baselines.
- [ ] **Step 3: Add the Refresh case test** `RefreshRegroupsTheOpenProposalUnderNeedsAnotherTry` in `ProposalsPageModelTests` (Review focus 2).
- [ ] **Step 4: Run the gate.**
- [ ] **Step 5: Commit** — "A walkthrough opens a Proposal from the Proposals page".

---

## Lane LC: review-list

**Scope.** §7 and `apply --proposal`; the `review/<id>` reopen rule; `ReviewMarks`; keyboard; Help for
`proposal review` and `apply --proposal`; the `proposal-review-keep-undo` walkthrough.

**Tests.** Integration: `ProposalReviewTests`, `ReviewMarksTests`, `ApplyProposalTests`; Unit:
`ReviewPageModelTests` (rewritten), `KeyboardShortcutRegistryTests`; System: the walkthrough; parity and Help tests.

**Exit criteria.** Gate green; `motif proposal review --proposal <id> --json` prints grammar and word entries in
ordinal order with `lookedAt`; Undo on a rule in the window prompts for its dependents and leaves the Proposal needing
another try; `apply --all-pending` tests pass unchanged.

### Task C1: `ProposalReview` and the entry kinds

**Files:**
- Create: `src/SIL.Motif.Commands/Review/ProposalReview.cs`, `src/SIL.Motif.Contract/Responses/ProposalReviewResponse.cs`, `src/SIL.Motif.Contract/Requests/ProposalReviewRequest.cs`, `src/SIL.Motif.Help/Content/en/commands/proposal-review.md`
- Modify: `CommandCatalog.cs` (`proposal review` Released Read), `CliVerbCatalog.cs`, `commands.json`
- Test: `tests/SIL.Motif.Tests.Commands/Commands/ProposalReviewTests.cs`

**Interfaces:**
- Consumes: L0 `Trials`, provenance `operationIds`, `PendingChanges.Snapshot` grouping, `RetirementReviewProjectionAssembler`.
- Produces: `ProposalReviewRequest(ProjectPath, string? ProposalId)`; `ProposalReviewResponse(ProposalId, IntentDigest, Revision, IReadOnlyList<ReviewEntry> Entries, TrialSummary? Trial, ReadinessProjection? Readiness, IReadOnlyList<ChangeFit> Fits, int LookedAtCount)`; `abstract record ReviewEntry(string EntryKey, int Ordinal, string Title, IReadOnlyList<string> OperationIds, IReadOnlyList<string> DependsOnEntries, string ContentDigest, bool LookedAt)`; `WordChangeEntry(…, PendingChange Change, ChangeFit Fit, Paired<ParserReading?> Reading)`; `GrammarChangeEntry(…, string Composer, string Summary, IReadOnlyList<EffectView> Effects, IReadOnlyList<RetirementAffectedReading> AffectedReadings, IReadOnlyList<RetirementReviewPart> Parts)`; `ICommandClient.ReadProposalReviewAsync`. Polymorphic JSON uses a `kind` discriminator (`word`, `grammar`).

- [ ] **Step 1: Write the failing tests** — `GrammarEntriesComeFirstInDependencyOrder`; `OneEntryPerComposerProvenance`; `AnUnclaimedOperationIsItsOwnEntry`; `WordEntriesFollowTextOrder`; `ARetirementIsOneEntryWithParts`; `NullProposalIdReadsThePendingDraft`; `ContentDigestChangesWhenAnOperationChanges`.
- [ ] **Step 2 to 5.** Commit — "A Proposal's changes are read as one ordered list".

### Task C2: review marks

**Files:**
- Create: `src/SIL.Motif.Commands/Review/ReviewMarks.cs`, `src/SIL.Motif.Contract/Requests/ReviewMarkRequest.cs`
- Modify: `src/SIL.Motif.Host/Store/MotifSchema.cs` (`ReviewMarks(ProposalId TEXT, EntryKey TEXT, ContentDigest TEXT, EvidenceDigest TEXT, LookedAtUtc TEXT, PRIMARY KEY(ProposalId, EntryKey))`, in the strict shape check), `CommandCatalog.cs` (`proposal looked-at` Developer HumanOnly), `CliVerbCatalog.cs`, `ProposalReview.cs` (joins marks)
- Test: `tests/SIL.Motif.Tests.Commands/Commands/ReviewMarksTests.cs`

**Interfaces:**
- Produces: `ReviewMarkRequest(ProjectPath, ProposalId, EntryKey, ContentDigest, EvidenceDigest, bool LookedAt)`; `ReviewMarks.Set(...)`, `ReviewMarks.Read(database, proposalId) : IReadOnlyDictionary<string, ReviewMark>`; `EvidenceDigest` = SHA-256 of `IntentDigest + "|" + BaselineToken JSON` of the Trial, or `"none"` without one; `ICommandClient.MarkLookedAtAsync`.

- [ ] **Step 1: Write the failing tests** — `AMarkSurvivesWhileContentAndEvidenceMatch`; `AMarkClearsWhenTheEntryContentChanges`; `AMarkClearsWhenTheTrialChanges`; `AMarkNamesNoPerson` (the row has exactly five columns); `AnOldStoreWithoutTheTableIsRefused`.
- [ ] **Step 2 to 5.** Commit — "Motif remembers which changes you looked at, and forgets when they change".

### Task C3: `apply --proposal`

**Files:**
- Modify: `src/SIL.Motif.Commands/PendingChangesWorkflow.cs` (`Apply(ApplyProposalRequest)`: finalize a Draft, Preflight, Dry Run from `Trials.LatestFor` requiring `Current`, Readiness from the Trial's Correctness, apply, Receipt; `apply --all-pending` calls it for `pending-changes`), `src/SIL.Motif.Contract/Requests/PendingChangesWorkflowRequests.cs` (`ApplyProposalRequest(ProjectPath, ProposalId, string? ExpectedRevision)`), `CommandCatalog.cs` (`apply --proposal` Released HumanOnly), `CliVerbCatalog.cs`, `commands.json`, `commands/apply-proposal.md`
- Test: `tests/SIL.Motif.Tests.Commands/Commands/ApplyProposalTests.cs`, existing `PendingChangesWorkflowTests` unchanged

- [ ] **Step 1: Write the failing tests** — `AppliesAFinalizedProposalFromItsCurrentTrial`; `FinalizesADraftThenApplies`; `RefusesWhenTheTrialIsNotCurrent` (`apply.trial-stale`); `RefusesWithoutATrial` (`apply.trial-missing`); `AllPendingKeepsItsResponseShape` (golden JSON unchanged).
- [ ] **Step 2 to 5.** Add the two codes to `ExpectedRefusalCodes`. Commit — "One Apply for any Proposal, from its Trial".

### Task C4: Review changes walks one list

**Files:**
- Modify: `src/SIL.Motif.App/ViewModels/ReviewPageModel.cs` (`Entries : IReadOnlyList<ReviewEntryViewModel>`; `Current`; `PreviousCommand`, `NextCommand`, `KeepCommand`, `UndoCommand`, `KeepAllCommand`, `UndoAllCommand`; `LookedAtText`; `UndoPrompt : UndoPromptViewModel?` naming dependents; Apply blockers "Try this Proposal again before Apply", "Nothing to apply"; `MeasureCommand` becomes `TryAgainCommand` delegating to the context), `ReviewListEntryViewModel.cs` (entry kinds), `RetirementProposalReviewViewModel.cs` (becomes the `GrammarChangeEntry` parts presenter), `Views/ReviewPanel.axaml` (toolbar Previous · Next · Keep all · Undo all · "2 of 4 looked at"; per-row Keep and Undo; the grammar entry template hosts `RetirementProposalReviewPanel`), `Tokens/Components/Review.axaml`, `KeyboardShortcutRegistry.cs` (Review scope: `Alt+Down` Next, `Alt+Up` Previous, `Alt+K` Keep, `Alt+Shift+U` Undo; A, D, U and Ctrl/⌘+Z unchanged), `AutomationIds.cs` (`ReviewPrevious`, `ReviewNext`, `ReviewKeep`, `ReviewUndo`, `ReviewLookedAt`, `ReviewUndoPrompt`), `Services/ICommandClient.PendingChanges.cs` (`RemoveOperationsAsync`, `ReopenAsync`)
- Test: `tests/SIL.Motif.Tests.App/App/ReviewPageModelTests.cs` (rewritten), `KeyboardShortcutRegistryTests`

**Interfaces:**
- Consumes: C1, C2, C3; L0 `TryAgainAsync`, `Lens`.
- Produces: the commands above; `UndoPromptViewModel(IReadOnlyList<string> Titles, IAsyncRelayCommand UndoAllOfThem, IRelayCommand Cancel)`.

- [ ] **Step 1: Write the failing tests** — `NextMovesAndMarksTheEntryItLeftLookedAt`; `PreviousMarksNothing`; `KeepMarksAndMovesToTheNextEntry` (revision unchanged); `UndoRemovesTheEntryClearsItsMarkAndNeedsAnotherTry`; `UndoAllMarksNothingAndEmptiesTheList`; `TheTallyDropsAnUndoneEntryFromTheDenominator`; `ApplyNeverRequiresTheTally` (zero looked at, Readiness true → Apply enabled); `UndoOfARuleWithDependentsPromptsWithTheirTitles`; `UndoOnAFinalizedProposalReopensItFirst`; `UndoOfTheLastEntryLeavesAnEmptyListAndBlocksApply` (Review focus 3); `LookedAtTextCounts`; `OutsideProposalModeTheListIsThePendingDraft`; `ApplyCallsApplyProposalForTheOpenProposal`.
- [ ] **Step 2 to 5.** Commit — "Review changes walks every change with Previous and Next, Keep and Undo".

### Task C5: Help and the walkthrough

**Files:**
- Create: `src/SIL.Motif.Help/Content/en/guide/review-a-proposal.md`, `walkthroughs/proposal-review-keep-undo.walkthrough.json`, its captions and baselines
- Modify: `guide/review-changes.md`, `guide/pending-changes.md` (one paragraph each), `WalkthroughReplayTests.cs` (wrapper), `media.json`
- Test: the walkthrough (`review-list`, `looked-at`, `undo-dependents`, `needs-another-try` captures)

- [ ] **Steps 1 to 5** as B3. Commit — "A walkthrough keeps and undoes changes in a Proposal".

---

## Lane LD: parse-in-proposal

**Scope.** §8 and the word card's "with this Proposal" block (§5.4, Texts › Analyze texts row); the
`proposal-approve-parse` walkthrough.

**Tests.** Integration: `PendingChangesProposalTargetTests`, `ChangeFitPreflightEvidenceSetTests`; Unit:
`ChangesViewModelTests`, `ResultsTokenViewModelTests`, `WordCardTests`; System: the walkthrough.

**Exit criteria.** Gate green; a parse approved in Proposal mode appears in that Proposal's review list under the rule
that built it with a `DependsOnEntries` edge; the fit check reads the Trial's scratch; the pending Draft is untouched.

### Task D1: pending-change commands take a Proposal

**Files:**
- Modify: `src/SIL.Motif.Contract/Requests/PendingChangesRequests.cs` (`string? ProposalId = null` on Put, Remove, RemoveAnalysis, AcceptNewSet, Recheck, Reconfirm, PendingChanges), `src/SIL.Motif.Contract/Requests/ExpectedContext.cs` (`TrialEvidence? Trial` with `ProposalId`, `IntentDigest`, `RootAssessmentId`), `src/SIL.Motif.Worker.Runtime/Store/ExpectedContextRepository.cs` (a write carrying `Trial` is checked against the source Baseline token and the named Trial root, and is never accepted as the current Assessment), `src/SIL.Motif.Commands/PendingChanges.cs` (`DraftFor(repository, proposalId)`: null → `pending-changes`; a finalized Proposal → `reopen` as `review/<id>` on first write; `dependsOn` edges to creating operations when a reading's morphs name a `DraftOperation.EntityId` in the Draft)
- Test: `tests/SIL.Motif.Tests.Commands/Commands/PendingChangesProposalTargetTests.cs`

- [ ] **Step 1: Write the failing tests** — `PutIntoAProposalLeavesThePendingDraftUnchanged`; `PutIntoAFinalizedProposalReopensIt`; `AnApprovedReadingThatNamesACreatedAllomorphDependsOnItsCreation`; `RemoveOperationsOfTheRuleOffersTheDependentParse`; `AWriteCarryingTrialEvidenceIsAcceptedAgainstTheSourceBaselineAndTheTrialRoot`; `AWriteNamingATrialRootAsCurrentEvidenceIsRefused`.
- [ ] **Step 2 to 5.** Commit — "A staged analysis change can belong to any Proposal, and declares what it depends on".

### Task D2: the fit check reads the set

**Files:**
- Modify: `src/SIL.Motif.Commands/ChangeFitPreflight.cs` (`Check(…, EvidenceSet set)`: for a Trial set, open the Trial job's scratch when it exists, else mark every change `Uncertain` with reason `TrialScratchGone`), `ChangeFitReasons.cs`, `Contract/Responses/PendingChangesSnapshot.cs` (`ChangeUncertainty.Reason` value `trial-scratch-gone`)
- Test: `tests/SIL.Motif.Tests.Commands/Commands/ChangeFitPreflightEvidenceSetTests.cs`

- [ ] **Step 1: Write the failing tests** — `AParseThatOnlyExistsInTheTrialFitsAgainstTheScratch`; `WithoutTheScratchTheChangeIsUncertainAndSaysTryAgain` (Review focus 1).
- [ ] **Step 2 to 5.** Commit — "A parse approved inside a Proposal is checked against the Proposal's copy".

### Task D3: the word card's block and its two actions

**Files:**
- Modify: `src/SIL.Motif.App/ViewModels/ChangesViewModel.cs` (`TargetProposalId` follows `Context.Lens`), `ResultsTokenViewModel.cs` (`ProposalReading : Paired<…>`; `BuiltBy` entry titles from the review list), `ResultsInTextViewModel*.cs`, `AnalysisMarkingState.cs` (a `WithProposalReading` block), `WordDisposition.cs` (`ApproveInProposal`, `AddExpectedParse`), `Controls/WordPresentation/WordCard.cs` + `WordCard.axaml` (the tinted block), `Views/WordDispositionButtons.axaml`, `Tokens/Components/WordCard.axaml` (`.withProposal` using `Intent.Proposal.Fill/Text`)
- Test: `tests/SIL.Motif.Tests.App/App/ResultsTokenViewModelTests.cs`, `WordCardTests`, `ChangesViewModelTests`

- [ ] **Step 1: Write the failing tests** — `TheCardShowsInPanGlossWithThisProposalInProposalMode`; `ApproveInThisProposalStagesIntoTheOpenProposal`; `AddAnExpectedParseStagesAddAndApproveWithTheTrialsAssessmentId`; `BuiltByNamesTheGrammarEntry`; `OutsideProposalModeTheCardIsUnchanged`; `ATraceAgainstAGoneScratchOffersTryAgain` (Review focus 1, Try a Word).
- [ ] **Step 2 to 5.** Commit — "The word card shows what the Proposal builds, and lets you approve it there".

### Task D4: Help and the walkthrough

**Files:**
- Create: `walkthroughs/proposal-approve-parse.walkthrough.json`, captions, baselines (`word-card-with-proposal`, `staged-in-proposal`)
- Modify: `guide/change-an-analysis.md` (a section "Inside a Proposal"), `WalkthroughReplayTests.cs`, `media.json`

- [ ] **Steps 1 to 5** as B3. Commit — "A walkthrough approves a parse inside a Proposal".

---

## Lane LE: media-and-validation (last)

**Scope.** Regenerate everything derived, align the two architecture documents, and run the owner's final validation.

**Files:**
- Modify: `media.json` (every new PNG once, `generated` with `step: walkthroughs` for baselines and `site/fixtures`), `tests/SIL.Motif.Tests.Cli/Golden/help-all-current.json`, `docs/current-architecture.md` ("Reading stored evidence" gains the evidence-set paragraph; a "Proposal mode" paragraph under "Word display in the window"), `README.md` (one paragraph in Documentation naming the Proposals page)

- [ ] **Step 1:** `pwsh ./tools/Build-Media.ps1` (screenshots, walkthroughs, help, media-test); commit the outputs.
- [ ] **Step 2:** `pwsh ./test.ps1 -All -Configuration Release` with `MOTIF_PANGLOSS_EXE` and `MOTIF_SCREENSHOTS` set, on Windows, Ubuntu and macOS through CI.
- [ ] **Step 3:** Report the tested SHA, parser path and version, completed capture states, skipped categories and any Windows-only follow-up (AGENTS.md, "DONE for UI").
- [ ] **Step 4: Commit** — "Proposal review ships with its pictures, Help and architecture notes".

---

## Self-review

- **Spec coverage.** §3 → L0-1, L0-3; §4 → L0-2, L0-4; §5.1 → L0-6; §5.2, §5.3 → A2; §5.4 → A3, A4, A5, D3; §5.5 → A1;
  §6 → B1, B2; §7.1 → C1; §7.2 → C2, C4; §7.3 → C3, C4; §8 → D1, D2, D3; §9 → B1 (`NotAppliedYet`), C1 (null
  Proposal id), C3 (`--all-pending` unchanged); §10 → L0-2, A2, B3; §11 → A2, B2, D3; §12 → B1, B2, C1, C3, C5, D4, A6;
  §13 → B3, C5, D4, A6, E; §14 → C2, L0-4, L0-5; §15 → L0-7; §16 has no task by design.
- **Type consistency.** `Paired<T>` is Contract's in every lane; `TrialRecord`, `EvidenceSet`, `TrialSummary`,
  `ReviewEntry` names match between the lanes that produce and consume them; `OpenProposalAsync`, `TryAgainAsync`,
  `LeaveProposal` are L0's and used by B2, C4, A2.
- **Review focus.** 1 → D2, D3; 2 → A2, B3, L0-6; 3 → C4; 4 → B1; 5 → A1.
- **Proportion.** Signatures and test names, no bodies; each lane is one squashed commit of several task commits.
- **Research folded in.** Research-2: `Paired<T>.Moved`, `PairedOutputs`, the root `Classes.proposal` hook, stable
  rows (L0-6, A1, A2, A4). Research-3: the mark rules and the keyboard (C4). Research-4: the reader's explicit Trial
  binding and the `ExpectedContext.Trial` identity (L0-3, D1), the scratch-standing test (L0-4), two reader owners
  (L0-6), "not measured" rows (A4).
