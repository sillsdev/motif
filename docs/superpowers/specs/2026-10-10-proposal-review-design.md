# Proposal review: Proposal mode, the Proposals page, and one review list

**In plain terms:** Motif gets a Proposals page that lists every set of changes waiting for a person, with a scorecard
of what each one does to the numbers. Opening one puts the whole window into Proposal mode: a purple bar names the
Proposal, and every page the person already knows, Overview, Texts, Timing, Warnings and Review changes, shows
"today → with this Proposal" instead of today alone. Review changes then works like Word's Track Changes: Previous and
Next, Keep or Undo on each change, and a count of how many have been looked at. The person can approve a parse or add
an expected parse while inside the Proposal, so it travels with the grammar changes and is applied with them. The
changes a person has staged themselves, today's "changes not applied yet", are one Proposal among the others.

Underneath, this is one architectural move: the evidence a page shows gets a name, `EvidenceSet`, and every read takes
one. "Today" is the project's current EvidenceSet; "with this Proposal" is the EvidenceSet of the Proposal's latest
Trial. The window publishes the pair as an `EvidenceLens`, and a page built with no Proposal in mind keeps working
because the second half of the lens is simply absent. Nothing here records who made a Proposal or who looked at it.

Base commit: `9832cc60c`. Architecture scan: `_briefs/proposal-review/architecture-review.html` (candidates 1 to 8 are
cited below by number). Binding documents: `AGENTS.md`, `CONTEXT.md`, [ADR 0043](../../adr/0043-one-command-catalog-two-front-ends.md),
[ADR 0046](../../adr/0046-pages-not-stages.md), [ADR 0049](../../adr/0049-fieldworks-opinions-and-now-after-apply.md),
[ADR 0052](../../adr/0052-lazy-text-reads-share-one-evidence-context.md), [ADR 0057](../../adr/0057-explicit-allomorph-retirement.md),
[the Proposal lifecycle](../../proposal-lifecycle.md). The new decision is drafted as
[ADR 0061](../../adr/0061-proposal-mode-is-a-lens-over-one-set-of-pages.md).

## 0. Decisions, with their reasons

The brief delegated the design questions to the architect. Each was answered from the code and the brief; only the
questions in section 17 are left for the owner, each with the default this spec builds.

| # | Decision | Reason |
|---|---|---|
| D1 | The lens seam is a Commands record, `EvidenceSet`, resolved by `EvidenceSets`; the window publishes an `EvidenceLens` of one or two of them | ADR 0043: CLI and window share every read. Overview, Warnings, Inspector and Trace read "the current project" implicitly today and cannot be pointed at a Trial (candidate 1); naming the evidence once lets `motif overview --proposal` and the Overview page share the same numbers |
| D2 | A Trial becomes a readable record, `TrialRecord`, computed from the Jobs and Assessments rows that already hold it; no new table | Three callers rebuild "the latest Trial of this revision" from job JSON today, and the numbers MeasurePending computes are thrown away (candidate 2). ADR 0041 allows a read; it does not want a fourth copy |
| D3 | A Trial of a Proposal with any grammar operation measures the Default Selection; a Trial of a word-only Proposal measures its touched words | A grammar change can move any word, so a lens over touched words would mislead; an opinion change cannot move a parse, so the Default Selection would be waste. The Trial records its scope, and words it did not measure show "not measured with this Proposal" |
| D4 | Proposal mode is window state on `WorkspaceContext` (`Lens`), published to pages through the existing `OnEvidencePublishedAsync` hook with a changed parameter type | The hook already exists on every page and already carries "the evidence to show"; the owner's requirement that pages be built once is met by changing what the hook carries, not by adding a second hook or a second set of views |
| D5 | Before and after travel as `Paired<T>` values; a `Paired<T>` with no `After` renders as today's single value | One view per page (the canvas shows "7 → 8" in the same cell), and no conditional XAML for the degenerate case |
| D6 | Review changes reads one list, `ReviewEntry[]`, from a Commands module, `ProposalReview`; word changes and grammar changes are entry kinds of that list | Today word changes and the retirement review are two paths pasted into one page, so Previous and Next cannot walk both (candidate 4) |
| D7 | Keep is a review mark; Undo is `remove-operations`; both are over operation ids | Keep must not change the Proposal (rule 6 and the "no approval" rule); Undo must produce a new immutable revision, and `remove-operations` already does, honouring declared `dependsOn` (rule 5) |
| D8 | In the window, any Proposal under review is editable as a Draft; a finalized Proposal is reopened on the first Undo or staged parse; finalizing happens at Apply, as it does for pending changes today | The lifecycle statuses serve agents and the CLI; a linguist sees one thing, "this Proposal", and `PendingChangesWorkflow.Apply` already finalizes at Apply time |
| D9 | "Looked at" is kept in the Motif store, keyed by the entry's content digest and the Trial evidence it was judged against; it resets when either changes | Same rule as Read/Unread for words (CONTEXT.md, "Read"): a mark that survives a change to what it marked would lie. No person is recorded |
| D10 | The pending Draft (`pending-changes`) is listed as a Proposal, opened in Proposal mode, and reviewed through `ProposalReview`; `apply --all-pending` keeps its exact contract | ADR 0046 decision 3 already makes pending changes a Draft Proposal; the FieldWorks save-boundary contract must not move |
| D11 | A parse approved or added while in Proposal mode is staged into that Proposal's Draft, with `dependsOn` edges to any operation that creates an object the reading names | The owner wants it to travel with the grammar change as one package; rule 5 says order is declared, so the dependency is written down |
| D12 | Nothing re-runs on its own: after Refresh or after an Undo, a Proposal's Trial is stale and the person presses Try again | ADR 0035 and ADR 0046 decision 1 |
| D13 | Two Proposals that change the same words are flagged on the Proposals page; they are never merged | The canvas's open question; merging has no defined semantics under rules 5 and 6 |
| D14 | `proposals`, `trial`, `trial --wait`, `apply --proposal` and `proposal review` become Released commands with Help; review marks and `remove-operations` stay Developer and HumanOnly | ADR 0043 decision 3: every store effect the window has is a CLI verb; Released means a linguist-facing Help page, which these need and the marks do not |
| D15 | The `Decisions` table and the `User` argument of Apply are deleted; no migration (rule 18) | The owner's rule: no author is recorded, and none is needed (candidate 8) |
| D16 | A new Intent token family `Intent.Proposal.*` on new `Primitive.Color.Purple*` primitives, Semi's purple ramp; Violet stays Unread's | The brief; purple is a Semi colour a theme can change, and the token tests require Primitive-backed Intent keys |

## 1. Goals and non-goals

Goals: the five things in the brief (the Proposals page with a scorecard; Proposal mode as a context switch over the
existing pages; Review changes as Track Changes with grammar and word changes in one list; approving or adding a parse
inside a Proposal; pending changes as a Proposal), built so that a page, a scorecard metric or the review flow can
change cheaply later.

Non-goals, listed in section 16: Rebase, Check Runs, merging or comparing two Proposals, asking an AI assistant from
the window, any approval or author record, and any change to `apply --all-pending`'s contract.

## 2. Vocabulary

Additions to `CONTEXT.md` (the ADR carries the exact text):

- **Evidence set**: one named body of measured evidence a page or command reads: the Baseline it was measured against,
  its root ParseTime Assessment, the later re-runs that replace some of its words, its Correctness and per-rule timing
  measurements, its grammar check, and where it came from, the project or a Trial. The project's current evidence set
  and a Trial's evidence set have the same shape, so a page cannot tell them apart. _Avoid_: context, source, data
  context, snapshot.
- **Proposal mode**: the window's state while a Proposal is open: a context bar names it, and every page shows today's
  evidence set beside the Proposal's Trial evidence set. Leave returns to today. _Avoid_: compare mode, diff view,
  Proposal page, branch.
- **Looked at**: a review mark a person puts on one change in a Proposal with Keep or by stepping past it with Next.
  Kept in the Motif store, never in the Proposal; it clears when the change's content or the Trial evidence it was
  judged against changes. Nothing records who looked. _Avoid_: reviewed, accepted, approved, seen.
- **Keep** and **Undo** (in a Proposal): Keep marks a change looked at and leaves the Proposal as it is; Undo removes
  the change, and every change that declared it depended on, from the Proposal, which makes a new revision. Neither is
  Accept or Reject: nothing is approved, and an undone change is gone rather than refused. _Avoid_: accept, reject,
  approve, discard.

The window/CLI word table of ADR 0046 decision 3 gains these rows:

| In the window | In the CLI and in code |
|---|---|
| Proposals (the page) | `proposals` |
| Open a Proposal; Proposal mode | the `--proposal <id>` argument on reads; `EvidenceLens` in the App |
| Try again | `trial` |
| Needs another try | a stale Trial: its Baseline token or intent digest no longer matches |
| Looked at; N of M looked at | a review mark; `proposal looked-at` |
| Keep / Undo (a change) | a review mark / `remove-operations` |
| Your changes not applied yet | the `pending-changes` Draft |
| Apply to FieldWorks project | `apply --proposal` (and `apply --all-pending` for the pending Draft at a save boundary) |

The window never shows the words Draft, revision, intent digest, Trial, Preflight or EvidenceSet.

## 3. The lens seam: `EvidenceSet` and `EvidenceSets`

Lives in `src/SIL.Motif.Commands/Queries/EvidenceSet.cs` (the record is a Contract-free Commands type; the wire form a
CLI caller passes is the Proposal id, not the set).

```csharp
public enum EvidenceOriginKind { Project, Trial }

public sealed record EvidenceOrigin(EvidenceOriginKind Kind, string? ProposalId, string? IntentDigest, string? JobId);

public sealed record EvidenceSet(
    BaselineToken Baseline,                       // the Baseline measured against; a Trial carries its source Baseline's token
    string? RootAssessmentId,                     // ParseTime; null when nothing has been measured yet
    IReadOnlyList<string> ReplacementAssessmentIds,
    IReadOnlyDictionary<string, string> MeasurementIds,   // AssessmentKinds.Correctness, .ObjectTiming → id
    string? GrammarCheckId,                       // the stored grammar check of this set's grammar
    IReadOnlyList<string> MeasuredWords,          // what the root run and its replacements measured
    EvidenceOrigin Origin);
```

`EvidenceSets` resolves one:

```csharp
public static class EvidenceSets
{
    public static CommandOutcome<EvidenceSet?> Current(MotifDatabase database, ProjectLocator project);   // what CurrentEvidenceQuery selects today, named
    public static CommandOutcome<EvidenceSet?> ForTrial(MotifDatabase database, ProjectLocator project, TrialRecord trial);
}
```

`CurrentEvidenceQuery.ReadCurrentEvidence` keeps its signature and gains an `EvidenceSet? Set` on
`CurrentEvidenceSnapshot`; its selection logic (latest ParseTime for the current Baseline and resolved Default
Selection, same-invocation Correctness and ObjectTiming, replacements) moves into `EvidenceSets.Current` and is
called from there. `SelectionAssessmentEvidence` (`Selection/SelectionReader.cs:52`) is deleted; the reader takes an
`EvidenceSet`. The reader's token guard, `shown.Baseline == source.Baseline.Token`, is satisfied by a Trial set because
`TrialJobHandler.RecordAll` writes the source Baseline's token on every Trial Assessment, but
`ExpectedContextRepository.MatchesNamedRoot` and the Correctness and ObjectTiming lookups then require `ProposalId IS
NULL`, so a Trial root is rejected today *(research-4)*. The reader therefore gets an explicit Trial binding: a set
whose origin is a Trial is validated by its Proposal id, intent digest, source token and the exact companion ids the
set names, never by the current-Baseline rules, and it is never treated as the project's current evidence. A Trial
session reuses the source Baseline's `BaselineTextReadIndex` and Text and wordform detail, which is correct while the
Proposal leaves the chosen Texts and their occurrences intact; a Proposal that changes Text facts is out of scope.

**Every evidence read takes a set.** The request records in `SIL.Motif.Contract/Requests` gain one optional member,
`string? ProposalId`, which the command resolves to an EvidenceSet with `EvidenceSets.ForTrial(Trials.LatestFor(...))`;
null means `Current`. In-process callers that already hold a set pass it through a Commands-side overload. The reads:

| Read | Today | After |
|---|---|---|
| `OverviewCommand.Overview(OverviewRequest)` | path only, implicit current | `OverviewRequest(ProjectPath, ProposalId = null)`; `Overview(EvidenceSet)` overload |
| `StoredGrammarCheckQuery.Query(GrammarCheckRequest)` | current Baseline | reads `set.GrammarCheckId` |
| `TimingCommand` / `StatsCommand` | explicit `AssessmentId`, overrides | unchanged ids, taken from the set by the caller; refuse an id outside the set's Baseline as today |
| `WarningWordsQuery`, `StoredParseWarnings` | current snapshot | the set's root and replacements |
| `InspectQuery`, `WordTraceQuery` | current Baseline | the set's Baseline; a Trial's Trace runs against the Trial job's scratch when it still exists, else refuses "try again" |
| `SelectionReader` (ADR 0052) | `SelectionAssessmentEvidence` | `EvidenceSet` |
| `ReadWordContextAsync` | Baseline token | unchanged (a token) |

`AssessmentRepository` gains `GetHeader`-style reads that do not filter `ProposalId IS NULL`; the list queries that
select the project's own evidence keep that filter, because a Trial's rows must never become "current" by accident.

**The grammar check of a Trial.** `TrialJobHandler` runs `GrammarCheckQuery` over the scratch grammar after the Dry Run
and stores the result with the Trial's Baseline token and Proposal id, so Warnings can show "24 → 22" and "Gone with
this Proposal". Today's `GrammarCheckRepository.GetLatest(token)` gains a `ProposalId` column and an `IS NULL` filter
on the project path, exactly as `Assessments` has.

## 4. Trials as records

`src/SIL.Motif.Commands/Trials.cs`:

```csharp
// In Contract.Responses; freshness travels with the read result.
public enum TrialFreshness { Current, BaselineMoved, ProposalChanged, Incomplete, None }

public sealed record TrialRecord(
    string JobId, string ProposalId, string IntentDigest, BaselineToken Baseline,
    DryRunProjection? DryRun, IReadOnlyDictionary<string, string> AssessmentIds,   // kind → id
    IReadOnlyList<string> Words, bool AllWords, TrialLimits? Limits,
    JobStatus Status, DateTimeOffset? CompletedUtc, TrialFreshness Freshness);

public static class Trials
{
    public static TrialRecord? LatestFor(MotifDatabase database, ProjectLocator project, string proposalId);   // newest completed or running Trial job of this Proposal
    public static IReadOnlyList<TrialRecord> ListFor(MotifDatabase database, ProjectLocator project, string proposalId);
}
```

`TrialRecord` keeps its durable job fields positional. Its read projection adds init-only `Label`, `GrammarCheckId`,
`WordResults` and `GrammarCheckWarning`; ordinary reads leave `WordResults` empty, and callers request word rows only
when a page opens a completed Trial. `TrialFreshness` lives in Contract because it crosses the command and window
boundary.

Freshness is computed at read time: `BaselineMoved` when the Trial's Baseline token differs from the current Baseline's;
`ProposalChanged` when the Trial's intent digest differs from `IntentDigest.Compute` of the Proposal's current content
(Draft JSON or finalized revision); `Incomplete` while the job runs or when it completed with an Assessment failure;
`None` when the Proposal has no Trial. The window word for `BaselineMoved` and `ProposalChanged` is "Needs another try".

Sources, unchanged: `Jobs` rows of kind `trial` (`InputJson` is the frozen Proposal and scope, `DryRunJson` its Dry
Run, `ResultJson` its `TrialCompletion`), and `Assessments` rows with `ProposalId` and `ProposalIntentDigest`.
`JobRepository.ListByProjectAndKind(projectKey, "trial")` (`src/SIL.Motif.Worker.Runtime/Jobs/JobRepository.cs`) already exists; `Trials` parses and joins.

The worker retains the scratch copy for each Proposal's latest Trial. A Trial completion removes earlier copies for
that Proposal, and startup removes copies whose job is gone or terminal and no longer latest.

Changes to the Trial itself:

- `EnqueueTrialRequest` keeps its shape. The window's Try again sends `AllWords: true` for a Proposal with any
  operation whose kind is not in the analysis groups (`AnalysisChangeKinds`), and the touched words otherwise (D3).
  `TrialLimits` default to the limits of the Assessment being compared with, as `MeasurePending` does today.
- `TrialJobHandler` adds the grammar check step (section 3) and records `AllWords` in `TrialCompletion`.
- `PendingChangesWorkflow.Measure` returns `MeasurePendingResult(TrialRecord Trial, ReviewNumbersResponse Numbers)`
  instead of a bare job id, so the pending Draft's Trial is a Trial like any other.
- `RetirementProposalReviewQuery.FindDryRunJobs` and `ProposalCommands.FindCandidateAssessment` read through `Trials`.
  The retirement review's Dry Run is the Trial's Dry Run; the standalone `dry-run` job stays for the CLI and MCP.
- `JobCommands.EnqueueDryRun` accepts a Draft, as `EnqueueTrial` does, by freezing `BuildProposalJson(draft)`. This
  removes the asymmetry that made the retirement review tests seed jobs by hand.

## 5. Proposal mode in the window

### 5.1 State

`WorkspaceContext` gains:

```csharp
public enum MarkupView { Today, TodayToProposal, Proposal }

public sealed record ProposalLens(string ProposalId, string Label, string IntentDigest, TrialRecord? Trial, MarkupView View);

[ObservableProperty] private ProposalLens? _lens;          // null is today
public EvidenceLens EvidenceLens { get; }                    // what pages receive
public Task OpenProposalAsync(string proposalId, CancellationToken ct);   // enters Proposal mode, loads the Trial, republishes
public void LeaveProposal();                                 // Lens = null, republishes
public Task TryAgainAsync(CancellationToken ct);             // EnqueueTrial for the open Proposal; progress on the top row
```

`EvidenceLens` (`ViewModels/EvidenceLens.cs`):

```csharp
public sealed class EvidenceLens : ObservableObject
{
    public ProjectEvidence Today { get; }                 // what every page receives today, unchanged
    public TrialEvidence? Proposal { get; }              // the Trial's EvidenceSet plus its words, Dry Run and freshness; null outside Proposal mode
    public MarkupView View { get; }
    public bool InProposalMode => Proposal is not null;
    public IReadOnlyList<EvidenceSet> Sets => Proposal is null ? [Today.Set] : [Today.Set, Proposal.Set];
    public Paired<T> Pair<T>(Func<EvidenceSet, T> read, Func<T, T, bool>? moved = null);   // reads once per set
    public Task<Paired<T>> PairAsync<T>(Func<EvidenceSet, CancellationToken, Task<CommandOutcome<T>>> read,
        Func<T, T, bool>? moved, CancellationToken ct);
}

public readonly record struct Paired<T>(T Before, T? After, bool HasAfter, bool Moved)
{
    public T Shown(MarkupView view);                             // Today → Before; Proposal → After ?? Before; TodayToProposal → Before (views bind both)
}
```

`Moved` is decided by a domain comparer the page passes (a count differs, a word changed cell, a rule's time moved
past a threshold); generic equality is not enough for every value *(research-2)*. A page that passes no comparer
gets `Moved == false`, and its value is paired but never highlighted.

`PageModel.OnEvidencePublishedAsync(EvidenceLens lens, CancellationToken)` replaces the `ProjectEvidence` parameter.
Every existing override compiles after replacing `evidence` with `lens.Today`; that is the mechanical part of the
cutover and is done in one commit before any page learns to pair.

`WorkspaceContext.RunPublicationAsync` reloads the Selection readers with the lens's sets and fans out the lens.
`WorkspaceSelection` becomes two reader owners, `Today` and `Proposal`, each with its own `Replacing` event and lease
accounting, because one owner disposes its old reader before opening the next and a side-by-side view needs both alive
*(research-4)*. The second session duplicates the compact index and bounded detail caches; that cost is accepted and
bounded by the existing per-session leases. `ChangesViewModel.AssessmentId` follows
`lens.Proposal?.Set.RootAssessmentId ?? lens.Today.ParseTimeAssessmentId`, so a parse approved in Proposal mode names
the Trial's Assessment (section 8).

### 5.2 Entering and leaving

Open comes from the Proposals page (one row's Open), from a sidebar badge, or from a deep link in a Receipt. Opening
reads `Trials.LatestFor`, sets `Lens`, republishes, and opens the Overview. The sidebar, project menu, Refresh and
Settings stay as they are. Leave is on the context bar; `Escape` with nothing else focused also leaves. Refresh in
Proposal mode captures a Baseline as today and marks the Trial `BaselineMoved`; nothing re-runs (D12). Opening
another project leaves Proposal mode first.

### 5.3 The context bar

A new root row of `MainWindow.axaml` between `TopBar` and the notices grid, full width, `Classes="proposalBar"`,
visible only in Proposal mode:

```
[●] Proposal: <label>    Tried <time> on Baseline of <save> · <freshness sentence>    [ Today | Today → Proposal | Proposal ]    [Try again]  [Leave]
```

Proposal mode is one class on the window root, `Classes.proposal="{Binding Context.EvidenceLens.InProposalMode}"`;
every mode-dependent style selects on `Window.proposal …` and takes its colours from `Intent.Proposal.*`, so the
Light and Dark theme variants are untouched. Flyouts and popups have their own roots, so the class is set on the
`FlyoutPresenter` classes too *(research-2)*. The three-way switch changes only how a `Paired<T>` is shown; "Today"
inside Proposal mode is a projection, not Leave, and nothing it hides is undone *(research-1)*.

Freshness sentences: "Current" ; "FieldWorks changed since this was tried. Try again." ; "This Proposal changed since
it was tried. Try again." ; "Trying…" (with the shared parse progress on the top row) ; "Not tried yet. Try it to see
what it does to the numbers." The bar's `AutomationId`s: `motif-proposal-bar`, `motif-proposal-bar-leave`,
`motif-proposal-bar-try-again`, `motif-proposal-bar-view`.

### 5.4 What each page shows

The canvas is the visual target (`canvas-summary.md`). The rule in every row: the page keeps its layout, and each
number it shows is a `Paired<T>` from `lens.PairAsync(...)` over the same query it runs today.

| Page | Today's read | In Proposal mode |
|---|---|---|
| Overview | `OverviewAsync(path)` | `PairAsync(set => OverviewAsync(path, set))`; tiles read "7 → 8 of 9 words parse"; Speed and Text coverage bars draw two rows; "Look first" becomes "What this Proposal moves", built from `DifferenceViewModel.Moves` over the two word lists, each row linking to the Matrix cell |
| Texts › Matrix | `Assess.Compare` over `Assess.Result` | `Compare` is loaded from `lens.Today`; a second `CompareViewModel` from the Trial words; cells show "Same 1 → 2"; a cell whose count moved gets `Classes="moved"`; a "Moved by this Proposal" filter chip lists words whose cell differs; the word list adds a "With this Proposal" column |
| Texts › Analyze texts, word card | one `AnalysisMarkingState` per word | the card adds an "In PanGloss · with this Proposal" block (tinted `Intent.Proposal.Fill`) from the Trial's reading for that word, with "Built by: <change>" links naming the ReviewEntry whose operations the reading's morphs reference; Fix ▾ gains "Approve <reading> in this Proposal" and "Add an expected parse…" (section 8) |
| Texts › What changed | previous in-session run vs this run | in Proposal mode the two lists are Today and With this Proposal; this tab is the Overview's "What this Proposal moves" in full |
| Timing | `TimingAsync(id, overrides)` | paired over the two sets' ids; stats read "795 → 801 ms"; kind bars draw Today and Proposal on one scale; the rule table's time column reads "288 → 291 ms"; a rule created by the Proposal is marked "new", one it removes "removed" (from the Dry Run's effects) |
| Warnings | `ReadStoredGrammarCheckAsync(path)` | paired over the two sets' grammar checks; chips "All 24 → 22", "Gone with this Proposal", "New with this Proposal"; a gone row is struck through and says "Fixed by: <change>" when the Dry Run touched the object the warning names |
| Review changes | the pending Draft | the open Proposal (section 7) |
| Try a Word | the current Baseline | traces against the Trial's scratch when it exists; otherwise the page says the Trial's copy is gone and offers Try again |
| Parsimony, AI Handoff | unchanged | unchanged; the bar still shows, the pages read `lens.Today` |

Lists keep their row view models and `ItemsSource` across a lens change and update the paired fields on the rows
they already hold, so selection, scroll position and realized containers survive entering or leaving Proposal mode;
large lists stay on a virtualizing `ListBox` *(research-2)*.

Sidebar badges in Proposal mode carry a small purple delta beside the page label, set by each page from its pair:
Texts "+1" (words that moved to Same), Timing "+2%", Warnings "−2", Review changes "4". `PageViewModel` gains
`DeltaBadge` next to `Badge`; the sidebar template shows it with `Classes="proposalDelta"`.

### 5.5 The seam test

Every page declares the values it pairs: `PageModel.PairedOutputs : IReadOnlyList<PairedOutput>` where
`PairedOutput(string Name, Func<IPaired> Read)`, registered in the page's constructor beside the property. The test
`tests/SIL.Motif.Tests.App/App/EvidenceLensContractTests.cs` builds every `PageRegistry` entry over a
`FakeCommandClient` scripted with a Today set and a Trial set whose values are distinct sentinels, publishes a lens
with both, and asserts for every page: no exception; every registered output has `HasAfter == true` and `Before !=
After`; every rendered `PairedText` shows both sentinels; publishing the same lens with `Proposal = null` gives the
same `Before` values, `HasAfter == false` and single-value formatting. A plain `T` property is allowed but is not an
output; a number that should pair and does not is caught in review, not by the test, because no test can recognise
an arbitrary scalar as evidence *(research-2)*.

## 6. The Proposals page and the `proposals` command

### 6.1 The command

`SIL.Motif.Commands/Catalog/ProposalStandingsCommand.cs`, catalogued as `proposals`, `CommandSurface.Released`,
`AgentClass.Read`, agent tool `motif_proposals` (replacing `list` as that tool's target; `list` and `show` stay
Developer). Request `ProposalStandingsRequest(string ProjectPath, bool IncludeSetAside = false)`. Response, in
Contract:

```csharp
public enum ProposalGroup { WaitingForYou, NeedsAnotherTry, NotAppliedYet, SetAside }   // SetAside = applied, rejected, superseded, deferred

public sealed record ProposalStandings(IReadOnlyList<ProposalCard> Proposals, int SetAsideCount);

public sealed record ProposalCard(
    string ProposalId, string Label, string Status, ProposalGroup Group, bool IsPendingDraft,
    IReadOnlyList<ChangeKindCount> ChangeKinds,            // "rule" 2, "allomorph" 1, "parse approved" 1 — from ReviewEntry kinds
    TrialSummary? Trial,                                   // null when never tried
    IReadOnlyList<string> AlsoChangedBy);                  // other Proposal ids that change one of the same words (D13)

public sealed record TrialSummary(
    string JobId, DateTimeOffset? CompletedUtc, string Freshness, bool AllWords,
    Paired<double?> TextCoveragePercent, Paired<int> ApprovedKept, Paired<int> ApprovedTotal,
    Paired<double?> WordTimeMs, Paired<int?> WarningCount,
    int LostApproved, int NoLongerFits);                  // the chips
```

`Paired<T>` is defined once, in Contract (`SIL.Motif.Contract/Responses/Paired.cs`), and the App uses that type. The
numbers are the Overview's own: `OverviewMetrics.Build` and `TimingAggregation.SummarizeWords` run over each set's
effective words; `WordsLosingApprovedAnalysis` comes from `ReviewNumbersCommand`. Group rules: a Proposal with a
`Current` complete Trial is `WaitingForYou`; `BaselineMoved`, `ProposalChanged` or `None` is `NeedsAnotherTry`
(a never-tried Proposal needs a first try); the `pending-changes` Draft is `NotAppliedYet` whatever its Trial;
terminal statuses are `SetAside` and are returned only with `IncludeSetAside`.

### 6.2 The page

`WorkspacePage.Proposals`, second in the sidebar after Overview, label "Proposals", icon a stack of two cards.
`ProposalsPageModel : PageModel`, view `Views/Pages/ProposalsPage.axaml`, built from Review changes' grouped-card
style (`Tokens/Components/Review.axaml` classes, plus a `Tokens/Components/Proposals.axaml`). Groups in order:
"Waiting for you", "Needs another try", "Your changes not applied yet", then a link "Applied and set aside (N)" that
loads the rest. A row: label; change kinds in one line ("2 rules · 1 allomorph · 1 parse approved"); four numbers as
before → after; chips "1 lost", "1 no longer fits", "also changes words in <label>"; Open (and Try again on a
`NeedsAnotherTry` row). The page's badge is the `WaitingForYou` count. It reads `ProposalStandingsAsync` on
`OnProjectOpenedAsync`, `OnBaselineCapturedAsync`, `OnEvidencePublishedAsync` and when a Trial completes.

The side card the canvas shows ("Compare two Proposals", "Ask an AI assistant for one") is left out (section 16).

## 7. Review changes inside a Proposal

### 7.1 The list

`SIL.Motif.Commands/Review/ProposalReview.cs`, catalogued as `proposal review` (Released, Read; the CLI prints the
list, the window renders it):

```csharp
public sealed record ProposalReviewRequest(string ProjectPath, string? ProposalId);   // null = the pending Draft

public sealed record ProposalReviewResponse(
    string ProposalId, string IntentDigest, string Revision, IReadOnlyList<ReviewEntry> Entries,
    TrialSummary? Trial, ReadinessProjection? Readiness, IReadOnlyList<ChangeFit> Fits);

public abstract record ReviewEntry(string EntryKey, int Ordinal, string Title, IReadOnlyList<string> OperationIds,
    IReadOnlyList<string> DependsOnEntries, string ContentDigest, bool LookedAt);

public sealed record WordChangeEntry(... , PendingChange Change, ChangeFit Fit, Paired<ParserReading?> Reading) : ReviewEntry;
public sealed record GrammarChangeEntry(... , string Composer, string Summary, IReadOnlyList<EffectView> Effects,
    IReadOnlyList<RetirementAffectedReading> AffectedReadings, IReadOnlyList<RetirementReviewPart> Parts) : ReviewEntry;
```

One entry per composer provenance record. For that to hold, every composer provenance entry must carry its
`operationIds`; today only `RecordParsimonyDisposition` does (`ProposalCommands.SoundSystem.cs:372`), so the
composer path writes `operationIds` for every composer. An operation no provenance entry claims becomes its own
`GrammarChangeEntry` titled by its kind's Description. The pending Draft's word changes are `WordChangeEntry`s built
from the same per-`changeId` grouping `PendingChanges.Snapshot` does today. `ContentDigest` is the RFC 8785 digest of
the entry's operations; `EntryKey` is `changeId` for a word change and the first operation id for a grammar change.

Order: grammar entries first in declared dependency order (a rule before the retargets that depend on it), then word
entries in Text order as `ReviewPageModel.ReviewGroups` sorts them today. The ordinal is what Previous and Next walk.

### 7.2 Walking, Keep, Undo

`ReviewPageModel` keeps its Apply card and blockers and replaces `ReviewGroups` with `Entries` from `ProposalReview`.
New commands: `PreviousCommand`, `NextCommand`, `KeepCommand(entry)`, `UndoCommand(entry)`, `KeepAllCommand`,
`UndoAllCommand`; `LookedAtText` reads "2 of 4 looked at". Keyboard, in the Review scope of
`KeyboardShortcutRegistry`: `Alt+Down` Next, `Alt+Up` Previous, `Alt+K` Keep, `Alt+Shift+U` Undo; the bare letters
A, D and U keep their Opinion meaning and `Ctrl`/`⌘+Z` stays the staging undo *(research-3; resolves open question
7)*. The rules of the marks: Next marks the entry it leaves; Previous marks nothing; Keep marks and moves to the
next entry; Keep all marks every current entry; Undo and Undo all mark nothing and clear the marks of what they
removed; the tally counts only current entries whose content and evidence digests still match, and an undone entry
leaves the denominator. The tally is attention, not progress toward Apply: Apply never requires it, and a Proposal
with no entry looked at applies when its evidence says it can *(research-1, Phabricator's reason for an optional
Done; research-3)*.

Keep: `proposal looked-at` (`ReviewMarkRequest(ProjectPath, ProposalId, EntryKey, ContentDigest, EvidenceDigest,
bool LookedAt)`), Developer, HumanOnly. Storage: one new table,
`ReviewMarks(ProposalId, EntryKey, ContentDigest, EvidenceDigest, LookedAtUtc)`, primary key
`(ProposalId, EntryKey)`. `EvidenceDigest` is the Trial's intent digest plus Baseline token, so a mark made against
one Trial does not survive another. `ProposalReview` joins the marks and reports `LookedAt` only when both digests
still match. No person is recorded (D9, D15).

Undo: if the Proposal is finalized, `reopen` it under the Draft name `review/<proposalId>`; then
`remove-operations(DraftName, entry.OperationIds, Force: false)`. When `OperationDependencyGraph.TransitiveDependents`
is non-empty, the window shows "Undoing this also undoes: <titles>" with Undo all of them or Cancel; the CLI refuses
without `--force` as it does today. The Draft's revision changes, `Trials.LatestFor` reports `ProposalChanged`, the
bar says "This Proposal changed since it was tried. Try again.", and the Apply card's first blocker is "Try this
Proposal again before Apply". Marks on the remaining entries survive, because their content digest did not change (D9).
Undo all removes every entry in reverse ordinal order through one `remove-operations` call.

### 7.3 Apply

The Apply card lists the same blockers as today plus the Trial blockers: not tried, needs another try, a lost approved
analysis (from `TrialSummary.LostApproved`), a change that no longer fits. Its button stays "Apply to FieldWorks
project" and calls `apply --proposal <id>`: a new Released, HumanOnly catalog command with request
`ApplyProposalRequest(ProjectPath, ProposalId, string? ExpectedRevision)` whose implementation is
`PendingChangesWorkflow.Apply` generalized over a Proposal id: finalize a Draft if it is one, Preflight per change,
take the Dry Run from the current Trial (`Trials.LatestFor`, refusing when it is not `Current`), check Readiness
from the Trial's Correctness, apply in one LibLCM unit of work, write the Receipt. `apply --all-pending` calls the
same implementation for the `pending-changes` Draft and keeps its response, exit code and JSON fields. The `User`
argument is deleted (D15). After Apply the window records `AppliedSinceRefresh`, leaves Proposal mode and shows the
Receipt on Review changes as today.

## 8. Approving or adding a parse inside a Proposal

In Proposal mode, the word card's "In PanGloss · with this Proposal" block offers two actions through the existing
`WordDispositionButtons` and `Fix ▾`:

- **Approve <reading> in this Proposal**: the Trial built a reading that matches an analysis FieldWorks holds as
  Unknown. Stages `approve` for that stored analysis into the open Proposal's Draft.
- **Add an expected parse…**: the Trial built a reading FieldWorks does not hold. Stages `add-candidate` plus
  `approve` (shown as "Will add as Approved", ADR 0049's verb) with `AssessmentId` = the Trial's root Assessment.

Mechanics:

- `ChangesViewModel` gets a `TargetDraft` (`pending-changes` outside Proposal mode; `review/<proposalId>` inside,
  reopening a finalized Proposal on first use as in section 7.2). `PutPendingChangeRequest`, `RemovePendingChangeRequest`
  and their siblings gain `string? ProposalId` (null = pending). `PendingChanges` resolves the Draft by it.
- `PendingChanges.Put` composes the analysis operations as today. When the reading's morphs name an entity id that an
  operation in the same Draft creates (`DraftOperation.EntityId`), the composed operations declare `dependsOn` that
  operation (D11, rule 5). `OperationDependencyGraph.AllEdges` then sees the edge, so Undo of the rule offers to undo
  the parse with it.
- The write guard, `ExpectedContext`, rejects a Proposal-owned Assessment as "current" today *(research-4)*. It
  gains `TrialEvidence(ProposalId, IntentDigest, RootAssessmentId)` beside its Baseline token: a write made in
  Proposal mode carries the source Baseline's identity and the Trial's, and is checked against both without the
  Trial ever being mistaken for the current Assessment.
- The change's fit fingerprint is checked against the Trial's scratch, not the Baseline, because the analysis may
  only exist there. `ChangeFitPreflight` takes the `EvidenceSet` (section 3); for a Trial set it reads the Trial
  job's scratch when present and otherwise marks the change `Uncertain` with the reason "the Proposal's copy is gone;
  try again".
- Apply writes the grammar operations and the analysis in one unit of work; the approved analysis is then an Approved
  expectation (CONTEXT.md) measured by the next Assessment.

Outside Proposal mode nothing changes: the pending Draft receives the change and the Trial's Assessment id is not
involved.

## 9. Pending changes as a Proposal

The `pending-changes` Draft already is a Proposal row. This spec makes it visible as one:

- `ProposalStandings` lists it under "Your changes not applied yet" with the label "Changes to word analyses" it
  already carries, its change kinds ("3 approved · 1 added · 1 removed") and its latest Trial's numbers (MeasurePending's
  Trial, section 4).
- Opening it enters Proposal mode like any other Proposal. Its word changes cannot move a parse, so the Overview and
  Timing pairs show equal values; the Matrix shows the "now → after Apply" marks it shows today, and Review changes
  shows the same list it shows outside Proposal mode.
- Review changes outside Proposal mode keeps showing the pending Draft, through `ProposalReview.Read(ProposalId: null)`.
  The sidebar badge stays its change count.
- `apply --all-pending` is unchanged in contract and keeps finalizing the pending Draft at a save boundary.

## 10. Staleness and Refresh

Two independent facts, both computed, never stored as state: the project moved (Refresh captured a new Baseline, or
FieldWorks saved since) and the Proposal moved (its content digest changed). `TrialFreshness` names them; the Proposals
page groups on them; the context bar words them; the Apply card blocks on them. Try again is the only remedy and is
always the person's act. After Refresh, the pending Draft's fit check runs as today (`Changes.RecheckAsync`); other
Proposals' per-change fits are checked when they are opened.

## 11. Tokens

`Primitives.axaml` gains Semi's purple ramp as `Primitive.Color.Purple25`, `Purple200`, `Purple300`, `Purple500`
(`#9E28B3`, Semi purple-5, the owner's bar colour), `Purple600`, `Purple800`, `Purple975`, following the Violet
steps. `Intent.axaml` gains, in both theme dictionaries and in the header list:

| Key | Meaning |
|---|---|
| `Intent.Proposal.Bar` | the context bar's fill |
| `Intent.Proposal.Bar.Text` | text and controls on the bar |
| `Intent.Proposal.Accent` | the outline of a moved Matrix cell, the delta badge, the "with this Proposal" marker |
| `Intent.Proposal.Fill` | the tint behind a "with this Proposal" block or row |
| `Intent.Proposal.Text` | readable text on `Intent.Proposal.Fill` |
| `Intent.Proposal.Badge.Fill`, `Intent.Proposal.Badge.Text` | the sidebar delta badge |

Component files: `Tokens/Components/ProposalBar.axaml` (the bar, its three-way switch, Try again, Leave),
`Tokens/Components/Proposals.axaml` (the page's rows and chips), and a `paired` class set in
`Tokens/Components/Text.axaml` for the "7 → 8" rendering (`TextBlock.pairedBefore`, `.pairedArrow`, `.pairedAfter`).
Each new component file gets its `StyleInclude` in `App.axaml`, a `ComponentStyleTests.Cases()` entry, and a
`ComponentStateContractCases` row for every interaction selector. Violet remains Unread's and the phonological-rule
timing colour's; no view names a Primitive.

## 12. Help text

Released commands and their Help pages (`commands.json` entry with Title ≤ 30 characters and a one-to-three sentence
Description; page at `commands/<slug>.md` with When to use it, Example, What it prints, Related commands):

| Command | Title |
|---|---|
| `proposals` | List Proposals |
| `proposal review` | Read a Proposal's changes |
| `trial` | Try a Proposal |
| `trial --wait` | Wait for a Trial |
| `apply --proposal` | Apply one Proposal |

Terms (`terms.json`, and `terms/<slug>.md` for the first two): `evidence-set`, `proposal-mode`, `looked-at`, `trial`
(today absent), `keep-and-undo`. Guide pages: `guide/proposals.md` (the page, the groups, Open and Try again),
`guide/proposal-mode.md` (the bar, the three-way switch, what each page shows, Leave), `guide/review-a-proposal.md`
(Previous and Next, Keep and Undo, looked at, Apply), and an update to `guide/review-changes.md` and
`guide/pending-changes.md` saying that pending changes are listed under Proposals. `HelpPopupViewModel.GuideCode`
maps `WorkspacePage.Proposals` to `proposals`; the page registry carries the guide code so the Help popup, the
automation id and the tests read it from one place (candidate 7, the registry part). `HelpCatalogTests`'s required
terms gain `trial` and `proposal-mode`. The CLI golden `help-all-current.json` is regenerated.

## 13. Walkthroughs and screenshots

Walkthroughs (`walkthroughs/*.walkthrough.json`, captions in `src/SIL.Motif.Help/Content/en/walkthroughs/`, one
wrapper class each in `WalkthroughReplayTests.cs`, baselines under `tests/SIL.Motif.Tests.App/Assets/WalkthroughBaselines/`,
`media.json` entries for every PNG):

| Id | Fixture | Captures |
|---|---|---|
| `proposals-open` | `proposal-ready` (new seeder: a Proposal with one sound rule authored through the composer, a completed fake-parser Trial over the Default Selection, plus the pending Draft with one approve) | `proposals` (the page with its three groups), `overview-with-proposal` (the bar and paired tiles) |
| `proposal-review-keep-undo` | `proposal-ready` | `review-list` (grammar and word entries in one list), `looked-at` (after Next and Keep), `undo-dependents` (the Undo prompt naming dependents), `needs-another-try` (the bar after Undo) |
| `proposal-approve-parse` | `proposal-ready` | `word-card-with-proposal` (the tinted block and Fix ▾), `staged-in-proposal` (Review changes showing the new word entry under the rule) |

Guide pages reference them with `shot:` links. `PageScreenshots.Views()` gains `("9-proposals", WorkspacePage.Proposals, …)`
and, through a new `OpenOverSampleDataInProposalMode()`, a Proposal-mode capture of Overview, Texts (Matrix), Timing,
Warnings and Review changes named `p-<n>-<page>`. `StateScreenshots.States()` gains the bar's three views and the Undo
prompt. The `MediaManifestTests` and `Build-Media.ps1` steps are unchanged in shape.

## 14. Store changes

- New table `ReviewMarks` (section 7.2). Added to the strict schema-shape validation; an older store is refused and
  recreated, pre-1.0 (rule 18).
- `GrammarChecks` stores `ProposalId` and `IntentDigest` for Trial checks so a later revision keeps its own warnings;
  its strict shape changes and older stores are refused and recreated under rule 18.
- `Decisions` is dropped (D15). Nothing reads or writes it today.
- No migration, no compatibility reader, no alias.

## 15. ADR and glossary

[ADR 0061](../../adr/0061-proposal-mode-is-a-lens-over-one-set-of-pages.md) records D1, D4, D6 to D9, D12 and D15, adds
the section 2 terms to `CONTEXT.md`, and amends ADR 0046 decision 3's word table. It also amends ADR 0046 decision 2:
the `Decisions` table that the withdrawn review Decision would have used is removed. No accepted ADR is contradicted:
ADR 0052's one reader keeps its role and gains a second session per lens; ADR 0057's four linked groups become one
`GrammarChangeEntry` with parts; ADR 0043's parity test covers the new commands.

## 16. Deliberately left out

- **Rebase** (rule 6 allows it; nothing here refreshes evidence or anchors automatically).
- **Check Runs** and any required-check policy; Readiness stays what Apply computes.
- **Comparing two Proposals** side by side, and **merging** them. Two Proposals that change the same words are flagged.
- **Asking an AI assistant** for a Proposal from the window; the "AI assistants" page some captures show belongs to the
  agent-packaging branch and is not in this base commit.
- **An author, an actor, an approval, a sign-off** of any kind, including on marks and Receipts.
- **Automatic reruns** of any kind.
- **Changing `apply --all-pending`**'s request, response, exit code or save-boundary semantics.
- **Inline markup of grammar text** in Word's style (strike-through of a rule's old environment, say); a grammar entry
  shows the Dry Run's before and after values as two columns.

## 17. Open questions for the owner

Each has a default the plan builds; none blocks.

1. **Sidebar position of Proposals.** Default: second, under Overview, because it is the way into Proposal mode.
2. **Trial scope for grammar Proposals.** Default: the whole Default Selection (D3). On a large project that is a
   full parse per Try again; the alternative, touched words only, would show a partial lens.
3. **Undo on an agent's finalized Proposal.** Default: reopen it as a Draft silently (D8). The alternative asks first.
4. **Default markup view.** Default: Today → Proposal, as Word's All Markup.
5. **Promoting `trial` to Released** (and so to `motif help`). Default: yes (D14).
6. **The two side cards** the canvas shows on Proposals, Compare two Proposals and Ask an AI assistant. Default: out
   of this effort.
7. **Keyboard.** Resolved by research-3: Alt+Down / Alt+Up for Next / Previous, Alt+K for Keep, Alt+Shift+U for
   Undo, leaving A, D, U and Ctrl/⌘+Z as they are (§7.2). No owner decision remains.

## 18. Risks

- **Two reader sessions.** ADR 0052's reader holds compact indexes per session; Proposal mode opens two. Both
  sessions read the source Baseline's index rows and Text detail and differ only in Assessment overlay; the compact
  index and bounded caches are held twice, bounded by the existing leases *(research-4, confirmed)*. The reader's
  current-Baseline validation must gain the explicit Trial binding of §3 or the second session cannot open.
- **A Trial that measured fewer words than a view shows.** A touched-words Trial is enough only for its touched words;
  every unmeasured row must read "not measured with this Proposal", never as a parse result *(research-4)*.
- **Trial grammar check cost.** A grammar check per Trial adds the check's time to Try again. It is the same check
  Refresh runs today.
- **Reopen on Undo changes a Proposal's status.** An agent polling `motif_proposals` sees `draft` where it left
  `proposed`. The `ProposalCard` says so; the lifecycle doc gains a sentence.
- **`Paired<T>` in Contract.** A new wire shape; RFC 8785 canonicalisation needs its two members named `before` and
  `after`, which the Contract tests pin.

## 19. What research changed

The four research reports (`_briefs/proposal-review/research-<n>-report.md`) arrived after the first draft. They
confirmed the lens shape (research-1 and research-2 both recommend one lens on the window over typed reads) and changed
these points, each marked inline above:

- `Paired<T>` carries `HasAfter` and a domain-decided `Moved`, and the lens takes the comparer (§5.1, research-2).
- Proposal mode is one `Classes.proposal` root class, also set on flyout roots; the three-way switch is a projection
  (§5.3, research-2 and research-1).
- Pages declare `PairedOutputs`, which the seam test iterates; row view models stay stable across lens changes (§5.4,
  §5.5, research-2).
- The Selection reader needs an explicit Trial binding because its root and measurement lookups filter Proposal-owned
  rows; a Trial session reuses the source Baseline's index (§3, §18, research-4). This corrects the first report's
  note that a Trial root opens unchanged.
- `ExpectedContext` carries Trial evidence identity for writes made in Proposal mode (§8, research-4).
- The mark rules for Previous, Undo and Undo all, the tally rule, and the keyboard (§7.2, research-3); open question 7
  is resolved.
